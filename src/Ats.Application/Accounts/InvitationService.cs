using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Application.Common;
using Ats.Application.Email;
using Ats.Application.Integration;
using Ats.Application.Users;
using Ats.Domain.Entities;

namespace Ats.Application.Accounts;

public sealed record InviteDetails(string DisplayName, string Email);

public interface IInvitationService
{
    // Back office (Owner signed in): the user is read under the tenant filter.
    Task<OperationResult> SendInviteAsync(int userId, CancellationToken ct = default);

    // Rotates the stamp first, so every earlier link stops working.
    Task<OperationResult> ResendAsync(int userId, CancellationToken ct = default);

    // For the GET of a link: null when the link cannot be used. Changes nothing.
    Task<InviteDetails?> ReadUsableAsync(string? token, CancellationToken ct = default);

    // Sets the first password; on success the result carries what the caller needs to sign the user in.
    Task<SignInResult> AcceptAsync(string? token, string password, CancellationToken ct = default);
}

public sealed class InvitationService : IInvitationService
{
    public const string InvalidInvite = "This invitation is invalid or has expired. Ask your administrator for a new one.";
    public const string AlreadyAccepted = "This user has already accepted.";

    private readonly IUserRepository _users;
    private readonly IAccountStore _store;
    private readonly IAccountTokens _tokens;
    private readonly IEmailOutbox _outbox;
    private readonly IIdentityService _identity;
    private readonly ITenantBrandingService _tenant;
    private readonly ICurrentUser _current;
    private readonly string _baseUrl;

    // The link host comes from configuration only, never from the request (host header poisoning).
    public InvitationService(IUserRepository users, IAccountStore store, IAccountTokens tokens, IEmailOutbox outbox,
        IIdentityService identity, ITenantBrandingService tenant, ICurrentUser current, IntegrationOptions options)
    {
        _users = users; _store = store; _tokens = tokens; _outbox = outbox; _identity = identity; _tenant = tenant; _current = current;
        _baseUrl = options.CareerSiteBaseUrl.TrimEnd('/');
    }

    public async Task<OperationResult> SendInviteAsync(int userId, CancellationToken ct = default)
    {
        var (user, error) = await LoadPendingAsync(userId, ct);
        if (user is null) return OperationResult.Fail(error!);
        await StageInviteAsync(user, ct);
        await _users.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ResendAsync(int userId, CancellationToken ct = default)
    {
        var (user, error) = await LoadPendingAsync(userId, ct);
        if (user is null) return OperationResult.Fail(error!);
        user.SecurityStamp = Guid.NewGuid();
        await StageInviteAsync(user, ct);
        await _users.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<InviteDetails?> ReadUsableAsync(string? token, CancellationToken ct = default) =>
        await LoadByTokenAsync(token, ct) is { } u ? new InviteDetails(u.DisplayName, u.Email) : null;

    public async Task<SignInResult> AcceptAsync(string? token, string password, CancellationToken ct = default)
    {
        var user = await LoadByTokenAsync(token, ct);
        if (user is null) return Failed(InvalidInvite);
        if (UserService.PasswordError(password) is { } pwError) return Failed(pwError);

        user.PasswordHash = _identity.HashPassword(password);
        user.SecurityStamp = Guid.NewGuid();   // kills this link
        _store.Audit(user, "UserInviteAccepted", "Accepted the invitation and set a password");
        await _store.SaveChangesAsync(ct);
        return new SignInResult(true, user.Id, user.TenantId, user.Role, user.DisplayName, null, user.SecurityStamp, user.MustChangePassword);
    }

    private async Task<(AppUser? User, string? Error)> LoadPendingAsync(int userId, CancellationToken ct)
    {
        var user = await _users.GetAsync(userId, ct);
        if (user is null) return (null, "User not found.");
        if (!user.IsInvitePending) return (null, AlreadyAccepted);
        if (!user.IsActive) return (null, "Reactivate the user first.");
        return (user, null);
    }

    private async Task StageInviteAsync(AppUser user, CancellationToken ct)
    {
        var token = _tokens.Create(AccountTokenPurpose.Invite, new AccountToken(user.Id, user.TenantId, user.SecurityStamp));
        var link = $"{_baseUrl}/Account/AcceptInvite?token={Uri.EscapeDataString(token)}";
        var tenantName = (await _tenant.GetAsync(ct)).TenantName;
        _outbox.Stage(EmailTemplates.Invite(user.Email, user.DisplayName, tenantName, _current.Name ?? tenantName, link), user.TenantId);
    }

    private async Task<AppUser?> LoadByTokenAsync(string? token, CancellationToken ct)
    {
        var t = _tokens.Read(AccountTokenPurpose.Invite, token);
        if (t is null) return null;
        var user = await _store.GetAsync(t.TenantId, t.UserId, ct);
        return user is not null && user.IsActive && user.IsInvitePending && user.SecurityStamp == t.SecurityStamp
            && await _store.TenantActiveAsync(t.TenantId, ct)
            ? user : null;
    }

    private static SignInResult Failed(string error) => new(false, null, null, null, null, error);
}
