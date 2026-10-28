using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Application.Common;
using Ats.Application.Email;
using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Users;

// The user is created invite-pending (no password); IInvitationService sends the invite.
public sealed record CreateUserInput(string DisplayName, string Email, string Role);

public sealed record UpdateUserInput(int UserId, string DisplayName, string Email, string Role);

public static class UserField
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
}

// ChangedFields lists what was saved (UserField values, empty on failure or no-op) so the caller can
// write the audit summary without reading the user again. RemovedFromJobs counts the hiring teams a
// role change away from HiringManager removed the user from. PendingEmail is a checked new address for
// an accepted user that was NOT saved: the caller passes it to IEmailChangeService for confirmation.
public sealed record UserUpdateResult(OperationResult Result, IReadOnlyList<string> ChangedFields, int RemovedFromJobs = 0,
    string? PendingEmail = null)
{
    // A changed email or role rotates the security stamp, so the user's sessions end.
    public bool SignedOut => ChangedFields.Contains(UserField.Email) || ChangedFields.Contains(UserField.Role);
}

// Changed is false when the user already had the requested state (or on failure): nothing to audit.
public sealed record UserActiveResult(OperationResult Result, bool Changed);

public interface IUserService
{
    Task<UserListItem?> GetAsync(int id, CancellationToken ct = default);
    Task<(OperationResult Result, int? UserId)> CreateAsync(CreateUserInput input, CancellationToken ct = default);
    Task<UserUpdateResult> UpdateAsync(UpdateUserInput input, int actingUserId, CancellationToken ct = default);
    Task<UserActiveResult> SetActiveAsync(int userId, bool active, int actingUserId, CancellationToken ct = default);
    Task<OperationResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default);
}

public sealed class UserService : IUserService
{
    public const int MaxPasswordLength = 128;
    private const string LastOwner = "A workspace needs at least one active Owner.";
    private const string NotFound = "User not found.";
    private const string InvalidRole = "Choose a valid role.";

    private readonly IUserRepository _repo;
    private readonly IOnboardingStore _emails;   // global email check; email is unique across tenants
    private readonly IIdentityService _identity;
    private readonly IEmailOutbox _outbox;
    private readonly ITenantBrandingService _tenant;   // tenant name, read under the tenant filter

    public UserService(IUserRepository repo, IOnboardingStore emails, IIdentityService identity, IEmailOutbox outbox,
        ITenantBrandingService tenant)
    {
        _repo = repo; _emails = emails; _identity = identity; _outbox = outbox; _tenant = tenant;
    }

    public async Task<UserListItem?> GetAsync(int id, CancellationToken ct = default)
    {
        var u = await _repo.GetAsync(id, ct);
        return u is null ? null : new UserListItem(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.CreatedAt, u.IsInvitePending);
    }

    public async Task<(OperationResult Result, int? UserId)> CreateAsync(CreateUserInput input, CancellationToken ct = default)
    {
        var name = input.DisplayName?.Trim() ?? "";
        if (NameError(name) is { } nameError) return (OperationResult.Fail(nameError), null);
        var email = EmailAddress.Normalise(input.Email);
        if (!EmailAddress.IsValid(email)) return (OperationResult.Fail(EmailAddress.Invalid), null);
        if (!AtsRole.Assignable.Contains(input.Role)) return (OperationResult.Fail(InvalidRole), null);
        if (await _emails.EmailExistsAsync(email, ct)) return (OperationResult.Fail(EmailAddress.Duplicate), null);

        var id = await _repo.TryAddAsync(new AppUser
        {
            DisplayName = name,
            Email = email,
            Role = input.Role,
            PasswordHash = "",
            MustChangePassword = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        return id is null ? (OperationResult.Fail(EmailAddress.Duplicate), null) : (OperationResult.Ok, id);
    }

    // UpdateAsync and SetActiveAsync run the last-Owner count and the save in one serializable
    // transaction. When two Owners demote or deactivate each other at once, the count's range locks make
    // the two updates deadlock; SQL Server kills one (error 1205), the retry strategy treats that as
    // transient and re-runs it, and the re-run sees a single Owner and returns the LastOwner failure.
    public async Task<UserUpdateResult> UpdateAsync(UpdateUserInput input, int actingUserId, CancellationToken ct = default)
    {
        var name = input.DisplayName?.Trim() ?? "";
        if (NameError(name) is { } nameError) return UpdateFailed(nameError);
        var email = EmailAddress.Normalise(input.Email);
        if (!EmailAddress.IsValid(email)) return UpdateFailed(EmailAddress.Invalid);

        return await _repo.InTransactionAsync(async innerCt =>
        {
            var user = await _repo.GetAsync(input.UserId, innerCt);
            if (user is null) return UpdateFailed(NotFound);

            var nameChanged = name != user.DisplayName;
            var emailChanged = email != EmailAddress.Normalise(user.Email);
            // A stored role outside Assignable (set in the database) passes while it is left as it is.
            var roleChanged = input.Role != user.Role;

            if (input.UserId == actingUserId && (emailChanged || roleChanged))
                return UpdateFailed("You can only change your own name here.");
            if (roleChanged && !AtsRole.Assignable.Contains(input.Role)) return UpdateFailed(InvalidRole);

            // An invite-pending user has never used the address, so it changes at once (a new invite follows).
            var emailPending = emailChanged && !user.IsInvitePending;
            if (emailPending && !user.IsActive) return UpdateFailed("Reactivate the user before changing their email address.");

            var changed = new List<string>(3);
            if (nameChanged) changed.Add(UserField.Name);
            if (emailChanged && !emailPending) changed.Add(UserField.Email);
            if (roleChanged) changed.Add(UserField.Role);
            var result = new UserUpdateResult(OperationResult.Ok, changed, PendingEmail: emailPending ? email : null);
            if (!nameChanged && !emailChanged && !roleChanged) return result;

            if (roleChanged && await IsLastActiveOwnerAsync(user, innerCt)) return UpdateFailed(LastOwner);
            if (emailChanged && await _emails.EmailExistsAsync(email, innerCt)) return UpdateFailed(EmailAddress.Duplicate);
            if (changed.Count == 0) return result;

            // Switching back to HiringManager later must not silently restore old assignments. Deactivation
            // keeps the links: a deactivated user cannot sign in.
            if (roleChanged && user.Role == AtsRole.HiringManager)
                result = result with { RemovedFromJobs = await _repo.RemoveHiringTeamLinksAsync(user.Id, innerCt) };

            if (nameChanged) user.DisplayName = name;
            if (emailChanged && !emailPending) user.Email = email;
            if (roleChanged) user.Role = input.Role;
            if (result.SignedOut) user.SecurityStamp = Guid.NewGuid();
            return await _repo.TrySaveAsync(innerCt) ? result : UpdateFailed(EmailAddress.Duplicate);
        }, ct);
    }

    public async Task<UserActiveResult> SetActiveAsync(int userId, bool active, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return ActiveFailed("You cannot deactivate your own account.");

        return await _repo.InTransactionAsync(async innerCt =>
        {
            var user = await _repo.GetAsync(userId, innerCt);
            if (user is null) return ActiveFailed(NotFound);
            if (user.IsActive == active) return new UserActiveResult(OperationResult.Ok, false);
            if (!active && await IsLastActiveOwnerAsync(user, innerCt)) return ActiveFailed(LastOwner);

            user.IsActive = active;
            user.SecurityStamp = Guid.NewGuid();
            // An invite-pending user never had access, so there is nothing to tell them.
            if (!active && !user.IsInvitePending)
            {
                var tenantName = (await _tenant.GetAsync(innerCt)).TenantName;
                _outbox.Stage(EmailTemplates.AccountDeactivated(user.Email, user.DisplayName, tenantName), user.TenantId);
            }
            await _repo.SaveChangesAsync(innerCt);
            return new UserActiveResult(OperationResult.Ok, true);
        }, ct);
    }

    public async Task<OperationResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _repo.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail(NotFound);
        if (!_identity.VerifyPassword(user.PasswordHash, currentPassword ?? ""))
            return OperationResult.Fail("Current password is incorrect.");
        if (PasswordError(newPassword) is { } pwError) return OperationResult.Fail(pwError);
        if (newPassword == currentPassword) return OperationResult.Fail("Choose a password different from the current one.");

        user.PasswordHash = _identity.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();   // signs out any other session; the caller re-issues this one
        var tenantName = (await _tenant.GetAsync(ct)).TenantName;
        _outbox.Stage(EmailTemplates.PasswordChanged(user.Email, user.DisplayName, tenantName), user.TenantId);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    // An invite-pending Owner is not counted, so removing one never leaves the workspace without an Owner.
    private async Task<bool> IsLastActiveOwnerAsync(AppUser user, CancellationToken ct) =>
        user.Role == AtsRole.Owner && user.IsActive && !user.IsInvitePending && await _repo.CountActiveOwnersAsync(ct) <= 1;

    // No minimum length for now (decision 1 October 2026; to be reinstated). The maximum stays because
    // hashing very long inputs is a denial-of-service risk. Shared with AccountRecoveryService.
    public static string? PasswordError(string? password) =>
        string.IsNullOrWhiteSpace(password) ? "Enter a password."
        : password.Length > MaxPasswordLength ? $"Password must be {MaxPasswordLength} characters or fewer."
        : null;

    private static UserUpdateResult UpdateFailed(string error) => new(OperationResult.Fail(error), []);

    private static UserActiveResult ActiveFailed(string error) => new(OperationResult.Fail(error), false);

    private static string? NameError(string name) =>
        name.Length == 0 ? "Name is required."
        : name.Length > 200 ? "Name must be 200 characters or fewer."
        : null;
}
