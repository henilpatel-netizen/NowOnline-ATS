using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Application.Common;
using Ats.Application.Email;
using Ats.Application.Integration;
using Ats.Application.Users;
using Ats.Domain.Entities;

namespace Ats.Application.Accounts;

public interface IEmailChangeService
{
    // Back office (Owner signed in), after IUserService.UpdateAsync returned PendingEmail. A second save that
    // rotates the stamp (signing the user out and killing every earlier verify link) and mails a link carrying
    // the new stamp to the new address and a notice to the old.
    Task<OperationResult> RequestAsync(int userId, string newEmail, int actingUserId, CancellationToken ct = default);

    // For the GET of a link: the new address, or null when the link cannot be used. Changes nothing.
    Task<string?> ReadUsableAsync(string? token, CancellationToken ct = default);

    // Sets the new address and rotates the stamp, which signs the user out everywhere.
    Task<OperationResult> ConfirmAsync(string? token, CancellationToken ct = default);
}

public sealed class EmailChangeService : IEmailChangeService
{
    public const string InvalidLink = "This link is invalid or has expired. Ask your administrator to change the address again.";

    private readonly IUserRepository _users;
    private readonly IAccountStore _store;
    private readonly IAccountTokens _tokens;
    private readonly IEmailOutbox _outbox;
    private readonly ITenantBrandingService _tenant;
    private readonly string _baseUrl;

    // The link host comes from configuration only, never from the request (host header poisoning).
    public EmailChangeService(IUserRepository users, IAccountStore store, IAccountTokens tokens, IEmailOutbox outbox,
        ITenantBrandingService tenant, IntegrationOptions options)
    {
        _users = users; _store = store; _tokens = tokens; _outbox = outbox; _tenant = tenant;
        _baseUrl = options.CareerSiteBaseUrl.TrimEnd('/');
    }

    public async Task<OperationResult> RequestAsync(int userId, string newEmail, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return OperationResult.Fail("You can only change your own name here.");
        var email = EmailAddress.Normalise(newEmail);
        if (!EmailAddress.IsValid(email)) return OperationResult.Fail(EmailAddress.Invalid);
        var user = await _users.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail("User not found.");
        if (!user.IsActive || user.IsInvitePending) return OperationResult.Fail("This user cannot confirm an email change.");

        // A new stamp kills the link of every earlier request (a mistyped address cannot confirm later) and signs
        // the user out. Saved with the mail below, so the token carries the stamp that is stored.
        user.SecurityStamp = Guid.NewGuid();
        var token = _tokens.Create(AccountTokenPurpose.EmailChange, new AccountToken(user.Id, user.TenantId, user.SecurityStamp, email));
        var link = $"{_baseUrl}/Account/ConfirmEmail?token={Uri.EscapeDataString(token)}";
        var tenantName = (await _tenant.GetAsync(ct)).TenantName;
        _outbox.Stage(EmailTemplates.EmailChangeVerify(email, user.DisplayName, tenantName, link), user.TenantId);
        _outbox.Stage(EmailTemplates.EmailChangeNotice(user.Email, user.DisplayName, tenantName, email), user.TenantId);
        await _users.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<string?> ReadUsableAsync(string? token, CancellationToken ct = default) =>
        (await LoadAsync(token, ct))?.NewEmail;

    public async Task<OperationResult> ConfirmAsync(string? token, CancellationToken ct = default)
    {
        if (await LoadAsync(token, ct) is not ({ } user, { } newEmail)) return OperationResult.Fail(InvalidLink);
        // The address was free when the Owner asked; another account may have taken it since.
        if (await _store.EmailTakenAsync(newEmail, ct)) return OperationResult.Fail(EmailAddress.Duplicate);

        var oldEmail = user.Email;
        user.Email = newEmail;
        user.SecurityStamp = Guid.NewGuid();   // kills this link and every session
        _store.Audit(user, "UserEmailChanged", $"Changed email from '{oldEmail}' to '{newEmail}' by confirmation link");
        return await _store.TrySaveAsync(ct) ? OperationResult.Ok : OperationResult.Fail(EmailAddress.Duplicate);
    }

    private async Task<(AppUser User, string NewEmail)?> LoadAsync(string? token, CancellationToken ct)
    {
        var t = _tokens.Read(AccountTokenPurpose.EmailChange, token);
        if (t?.NewEmail is not { } newEmail) return null;
        var user = await _store.GetAsync(t.TenantId, t.UserId, ct);
        return user is not null && user.IsActive && !user.IsInvitePending && user.SecurityStamp == t.SecurityStamp
            && await _store.TenantActiveAsync(t.TenantId, ct)
            ? (user, newEmail) : null;
    }
}
