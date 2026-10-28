using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Application.Common;
using Ats.Application.Email;
using Ats.Application.Integration;
using Ats.Application.Users;
using Ats.Domain.Entities;

namespace Ats.Application.Accounts;

public interface IAccountRecoveryService
{
    // Always completes silently, so the caller cannot tell whether the address has an account.
    Task RequestResetAsync(string email, CancellationToken ct = default);

    // For the GET of a link: decides between the form and the expired message, changes nothing.
    Task<bool> IsResetTokenUsableAsync(string? token, CancellationToken ct = default);

    Task<OperationResult> ResetAsync(string? token, string newPassword, CancellationToken ct = default);

    // Back office (Owner signed in): the user is read under the tenant filter. Changes neither the password
    // nor the security stamp, so the user stays signed in; deactivate a compromised account instead.
    Task<OperationResult> SendResetLinkAsync(int userId, int actingUserId, CancellationToken ct = default);
}

public sealed class AccountRecoveryService : IAccountRecoveryService
{
    public const string InvalidLink = "This link is invalid or has expired. Request a new one.";

    private readonly IAccountStore _store;
    private readonly IAccountTokens _tokens;
    private readonly IEmailOutbox _outbox;
    private readonly IIdentityService _identity;
    private readonly IUserRepository _users;
    private readonly ITenantBrandingService _tenant;
    private readonly string _baseUrl;

    // The link host comes from configuration only, never from the request (host header poisoning).
    public AccountRecoveryService(IAccountStore store, IAccountTokens tokens, IEmailOutbox outbox,
        IIdentityService identity, IUserRepository users, ITenantBrandingService tenant, IntegrationOptions options)
    {
        _store = store; _tokens = tokens; _outbox = outbox; _identity = identity; _users = users; _tenant = tenant;
        _baseUrl = options.CareerSiteBaseUrl.TrimEnd('/');
    }

    public async Task RequestResetAsync(string email, CancellationToken ct = default)
    {
        var user = await _store.FindByEmailAsync(email ?? "", ct);
        if (user is null || !MayReset(user) || !await _store.TenantActiveAsync(user.TenantId, ct)) return;
        await SendLinkAsync(user, await _store.TenantNameAsync(user.TenantId, ct), ct);
    }

    public async Task<bool> IsResetTokenUsableAsync(string? token, CancellationToken ct = default) =>
        await LoadAsync(token, ct) is not null;

    public async Task<OperationResult> ResetAsync(string? token, string newPassword, CancellationToken ct = default)
    {
        var user = await LoadAsync(token, ct);
        if (user is null) return OperationResult.Fail(InvalidLink);
        if (UserService.PasswordError(newPassword) is { } pwError) return OperationResult.Fail(pwError);

        user.PasswordHash = _identity.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();   // kills this link and every session
        _store.Audit(user, "UserPasswordReset", "Password reset by email link");
        _outbox.Stage(EmailTemplates.PasswordChanged(user.Email, user.DisplayName, await _store.TenantNameAsync(user.TenantId, ct)), user.TenantId);
        await _store.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    // Sends unconditionally, so it stays private: both callers decide first whether the user may get a link.
    private async Task SendLinkAsync(AppUser user, string tenantName, CancellationToken ct)
    {
        var token = _tokens.Create(AccountTokenPurpose.PasswordReset, new AccountToken(user.Id, user.TenantId, user.SecurityStamp));
        var link = $"{_baseUrl}/Account/ResetPassword?token={Uri.EscapeDataString(token)}";
        await _outbox.SendAsync(EmailTemplates.PasswordReset(user.Email, user.DisplayName, tenantName, link), user.TenantId, ct);
    }

    public async Task<OperationResult> SendResetLinkAsync(int userId, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return OperationResult.Fail("Use Change password for your own account.");
        var user = await _users.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail("User not found.");
        if (!user.IsActive) return OperationResult.Fail("Reactivate the user first.");
        if (user.IsInvitePending) return OperationResult.Fail("This user has not accepted their invitation yet. Use Resend invite.");
        await SendLinkAsync(user, (await _tenant.GetAsync(ct)).TenantName, ct);
        return OperationResult.Ok;
    }

    private async Task<AppUser?> LoadAsync(string? token, CancellationToken ct)
    {
        var t = _tokens.Read(AccountTokenPurpose.PasswordReset, token);
        if (t is null) return null;
        var user = await _store.GetAsync(t.TenantId, t.UserId, ct);
        return user is not null && MayReset(user) && user.SecurityStamp == t.SecurityStamp
            && await _store.TenantActiveAsync(t.TenantId, ct)
            ? user : null;
    }

    // An invite-pending user (no password yet) accepts the invite instead.
    private static bool MayReset(AppUser user) => user.IsActive && user.PasswordHash.Length > 0;
}
