using System.Net.Mail;
using Ats.Application.Abstractions;
using Ats.Application.Common;
using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Users;

public sealed record CreateUserInput(string DisplayName, string Email, string Role, string TemporaryPassword);

public sealed record UpdateUserInput(int UserId, string DisplayName, string Email, string Role);

public static class UserField
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
}

// ChangedFields lists what was saved (UserField values, empty on failure or no-op) so the caller can
// write the audit summary without reading the user again. RemovedFromJobs counts the hiring teams a
// role change away from HiringManager removed the user from.
public sealed record UserUpdateResult(OperationResult Result, IReadOnlyList<string> ChangedFields, int RemovedFromJobs = 0)
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
    Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword, int actingUserId, CancellationToken ct = default);
    Task<OperationResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default);
}

public sealed class UserService : IUserService
{
    public const int MaxPasswordLength = 128;
    private const string LastOwner = "A workspace needs at least one active Owner.";
    private const string NotFound = "User not found.";
    private const string DuplicateEmail = "That email address is already registered.";
    private const string InvalidEmail = "Enter a valid email address.";
    private const string InvalidRole = "Choose a valid role.";

    private readonly IUserRepository _repo;
    private readonly IOnboardingStore _emails;   // global email check; email is unique across tenants
    private readonly IIdentityService _identity;

    public UserService(IUserRepository repo, IOnboardingStore emails, IIdentityService identity)
    {
        _repo = repo; _emails = emails; _identity = identity;
    }

    public async Task<UserListItem?> GetAsync(int id, CancellationToken ct = default)
    {
        var u = await _repo.GetAsync(id, ct);
        return u is null ? null : new UserListItem(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.CreatedAt);
    }

    public async Task<(OperationResult Result, int? UserId)> CreateAsync(CreateUserInput input, CancellationToken ct = default)
    {
        var name = input.DisplayName?.Trim() ?? "";
        if (NameError(name) is { } nameError) return (OperationResult.Fail(nameError), null);
        var email = NormaliseEmail(input.Email);
        if (!IsEmail(email)) return (OperationResult.Fail(InvalidEmail), null);
        if (!AtsRole.Assignable.Contains(input.Role)) return (OperationResult.Fail(InvalidRole), null);
        if (PasswordError(input.TemporaryPassword) is { } pwError) return (OperationResult.Fail(pwError), null);
        if (await _emails.EmailExistsAsync(email, ct)) return (OperationResult.Fail(DuplicateEmail), null);

        var id = await _repo.TryAddAsync(new AppUser
        {
            DisplayName = name,
            Email = email,
            Role = input.Role,
            PasswordHash = _identity.HashPassword(input.TemporaryPassword),
            MustChangePassword = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        return id is null ? (OperationResult.Fail(DuplicateEmail), null) : (OperationResult.Ok, id);
    }

    // UpdateAsync and SetActiveAsync run the last-Owner count and the save in one serializable
    // transaction. When two Owners demote or deactivate each other at once, the count's range locks make
    // the two updates deadlock; SQL Server kills one (error 1205), the retry strategy treats that as
    // transient and re-runs it, and the re-run sees a single Owner and returns the LastOwner failure.
    public async Task<UserUpdateResult> UpdateAsync(UpdateUserInput input, int actingUserId, CancellationToken ct = default)
    {
        var name = input.DisplayName?.Trim() ?? "";
        if (NameError(name) is { } nameError) return UpdateFailed(nameError);
        var email = NormaliseEmail(input.Email);
        if (!IsEmail(email)) return UpdateFailed(InvalidEmail);

        return await _repo.InTransactionAsync(async innerCt =>
        {
            var user = await _repo.GetAsync(input.UserId, innerCt);
            if (user is null) return UpdateFailed(NotFound);

            var nameChanged = name != user.DisplayName;
            var emailChanged = email != NormaliseEmail(user.Email);
            // A stored role outside Assignable (set in the database) passes while it is left as it is.
            var roleChanged = input.Role != user.Role;

            if (input.UserId == actingUserId && (emailChanged || roleChanged))
                return UpdateFailed("You can only change your own name here.");
            if (roleChanged && !AtsRole.Assignable.Contains(input.Role)) return UpdateFailed(InvalidRole);

            var changed = new List<string>(3);
            if (nameChanged) changed.Add(UserField.Name);
            if (emailChanged) changed.Add(UserField.Email);
            if (roleChanged) changed.Add(UserField.Role);
            var result = new UserUpdateResult(OperationResult.Ok, changed);
            if (changed.Count == 0) return result;

            if (roleChanged && await IsLastActiveOwnerAsync(user, innerCt)) return UpdateFailed(LastOwner);
            if (emailChanged && await _emails.EmailExistsAsync(email, innerCt)) return UpdateFailed(DuplicateEmail);

            // Switching back to HiringManager later must not silently restore old assignments. Deactivation
            // keeps the links: a deactivated user cannot sign in.
            if (roleChanged && user.Role == AtsRole.HiringManager)
                result = result with { RemovedFromJobs = await _repo.RemoveHiringTeamLinksAsync(user.Id, innerCt) };

            if (nameChanged) user.DisplayName = name;
            if (emailChanged) user.Email = email;
            if (roleChanged) user.Role = input.Role;
            if (result.SignedOut) user.SecurityStamp = Guid.NewGuid();
            return await _repo.TrySaveAsync(innerCt) ? result : UpdateFailed(DuplicateEmail);
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
            await _repo.SaveChangesAsync(innerCt);
            return new UserActiveResult(OperationResult.Ok, true);
        }, ct);
    }

    public async Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return OperationResult.Fail("Use Change password for your own account.");
        if (PasswordError(temporaryPassword) is { } pwError) return OperationResult.Fail(pwError);
        var user = await _repo.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail(NotFound);
        if (!user.IsActive) return OperationResult.Fail("Reactivate the user first.");

        user.PasswordHash = _identity.HashPassword(temporaryPassword);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
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
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    private async Task<bool> IsLastActiveOwnerAsync(AppUser user, CancellationToken ct) =>
        user.Role == AtsRole.Owner && user.IsActive && await _repo.CountActiveOwnersAsync(ct) <= 1;

    // No minimum length for now (decision 1 October 2026; to be reinstated). The maximum stays because
    // hashing very long inputs is a denial-of-service risk.
    private static string? PasswordError(string? password) =>
        string.IsNullOrWhiteSpace(password) ? "Enter a password."
        : password.Length > MaxPasswordLength ? $"Password must be {MaxPasswordLength} characters or fewer."
        : null;

    private static UserUpdateResult UpdateFailed(string error) => new(OperationResult.Fail(error), []);

    private static UserActiveResult ActiveFailed(string error) => new(OperationResult.Fail(error), false);

    private static string? NameError(string name) =>
        name.Length == 0 ? "Name is required."
        : name.Length > 200 ? "Name must be 200 characters or fewer."
        : null;

    private static string NormaliseEmail(string? email) => email?.Trim().ToLowerInvariant() ?? "";

    private static bool IsEmail(string email) =>
        email.Length is > 0 and <= 256 && MailAddress.TryCreate(email, out var a) && a.Address == email;
}
