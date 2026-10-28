using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Application.Integration;
using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Accounts;

// An email change moves sign-in and every account link to a new mailbox: a wrong rule hands the account
// to an address nobody verified, or lets a dead link (used, stale, taken address) change it.
public class EmailChangeServiceTests
{
    private const string BaseUrl = "https://ats.example.test/";
    private const int TenantId = 4;
    private const int OwnerId = 1;
    private const string NewEmail = "new.ada@acme.test";

    private sealed class Ctx
    {
        public FakeUserRepository Repo { get; } = new();
        public FakeAccountStore Store { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeEmailOutbox Outbox { get; } = new();
        public EmailChangeService Service { get; }
        public UserService Users { get; }
        public InvitationService Invites { get; }
        public AppUser User { get; }

        public Ctx(string passwordHash = "hash:x")
        {
            Repo.Users.Add(new AppUser { Id = OwnerId, TenantId = TenantId, Email = "owner@acme.test", DisplayName = "Owner", Role = AtsRole.Owner, PasswordHash = "hash:o" });
            User = new AppUser
            {
                Id = 12,
                TenantId = TenantId,
                Email = "ada@acme.test",
                DisplayName = "Ada",
                Role = AtsRole.Recruiter,
                PasswordHash = passwordHash,
                IsActive = true
            };
            // The same tracked user, reached through the tenant-filtered repository (Owner signed in) and
            // through the account store (signed-out link).
            Repo.Users.Add(User);
            Store.Users.Add(User);
            var options = new IntegrationOptions { CareerSiteBaseUrl = BaseUrl };
            Service = new EmailChangeService(Repo, Store, Tokens, Outbox, new FakeTenantBranding(), options);
            Users = new UserService(Repo, new FakeOnboardingStore(), new FakeIdentityService(), Outbox, new FakeTenantBranding());
            Invites = new InvitationService(Repo, Store, Tokens, Outbox, new FakeIdentityService(), new FakeTenantBranding(),
                new FakeCurrentUser { Name = "Olivia Owner" }, options);
        }

        public string TokenFor(AppUser u, string? email = NewEmail) =>
            Tokens.Create(AccountTokenPurpose.EmailChange, new AccountToken(u.Id, u.TenantId, u.SecurityStamp, email));

        public string LinkToken(int index = 0) =>
            Uri.UnescapeDataString(Outbox.Staged[index].Draft.TextBody.Split("?token=")[1].Split(['\r', '\n', ' '])[0]);
    }

    [Fact]
    public async Task Requesting_stages_a_verify_link_to_the_new_address_and_a_notice_to_the_old_and_keeps_the_email()
    {
        var c = new Ctx();
        var result = await c.Service.RequestAsync(c.User.Id, NewEmail, OwnerId);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, c.Outbox.Staged.Count);
        var (verify, tenantId) = c.Outbox.Staged[0];
        Assert.Equal(NewEmail, verify.To);
        Assert.Equal(TenantId, tenantId);
        Assert.Contains("https://ats.example.test/Account/ConfirmEmail?token=", verify.TextBody);
        var notice = c.Outbox.Staged[1].Draft;
        Assert.Equal("ada@acme.test", notice.To);
        Assert.DoesNotContain("?token=", notice.TextBody);
        Assert.Contains(NewEmail, notice.TextBody);
        Assert.Equal("ada@acme.test", c.User.Email);
        Assert.Equal(1, c.Repo.SaveCount);

        var token = c.Tokens.Read(AccountTokenPurpose.EmailChange, c.LinkToken());
        Assert.Equal(NewEmail, token!.NewEmail);
        Assert.Equal(c.User.SecurityStamp, token.SecurityStamp);
    }

    // Each request rotates the stamp, which also signs the user out (as a direct email change did before).
    [Fact]
    public async Task Requesting_rotates_the_stamp()
    {
        var c = new Ctx();
        var stamp = c.User.SecurityStamp;

        Assert.True((await c.Service.RequestAsync(c.User.Id, NewEmail, OwnerId)).Succeeded);

        Assert.NotEqual(stamp, c.User.SecurityStamp);
    }

    // A typo can be withdrawn: whoever controls the mistyped address must not be able to confirm it later.
    [Fact]
    public async Task A_new_request_kills_the_link_of_the_previous_one()
    {
        var c = new Ctx();
        Assert.True((await c.Service.RequestAsync(c.User.Id, "typo.ada@acme.test", OwnerId)).Succeeded);
        var first = c.LinkToken(0);
        Assert.True((await c.Service.RequestAsync(c.User.Id, NewEmail, OwnerId)).Succeeded);
        var second = c.LinkToken(2);

        Assert.Null(await c.Service.ReadUsableAsync(first));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(first)).Error);
        Assert.Equal("ada@acme.test", c.User.Email);

        var confirm = await c.Service.ConfirmAsync(second);
        Assert.True(confirm.Succeeded, confirm.Error);
        Assert.Equal(NewEmail, c.User.Email);
    }

    [Fact]
    public async Task Requesting_for_self_an_unknown_a_deactivated_or_an_invite_pending_user_stages_nothing()
    {
        var c = new Ctx();
        Assert.False((await c.Service.RequestAsync(c.User.Id, NewEmail, c.User.Id)).Succeeded);
        Assert.False((await c.Service.RequestAsync(99, NewEmail, OwnerId)).Succeeded);
        Assert.False((await c.Service.RequestAsync(c.User.Id, "not an email", OwnerId)).Succeeded);
        c.User.IsActive = false;
        Assert.False((await c.Service.RequestAsync(c.User.Id, NewEmail, OwnerId)).Succeeded);
        c.User.IsActive = true;
        c.User.PasswordHash = "";
        Assert.False((await c.Service.RequestAsync(c.User.Id, NewEmail, OwnerId)).Succeeded);
        Assert.Empty(c.Outbox.Staged);
        Assert.Equal(0, c.Repo.SaveCount);
    }

    // The controller calls UserService.UpdateAsync, then RequestAsync. A role change in the same save
    // rotates the stamp, so the token must be minted after that save or it is dead on arrival.
    [Fact]
    public async Task A_role_and_email_change_together_yields_a_link_that_confirms()
    {
        var c = new Ctx();
        var update = await c.Users.UpdateAsync(new UpdateUserInput(c.User.Id, "Ada", NewEmail, AtsRole.Viewer), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.True(update.SignedOut);
        Assert.Equal(NewEmail, update.PendingEmail);

        Assert.True((await c.Service.RequestAsync(c.User.Id, update.PendingEmail!, OwnerId)).Succeeded);
        var token = c.Tokens.Read(AccountTokenPurpose.EmailChange, c.LinkToken());
        Assert.Equal(c.User.SecurityStamp, token!.SecurityStamp);

        var confirm = await c.Service.ConfirmAsync(c.LinkToken());
        Assert.True(confirm.Succeeded, confirm.Error);
        Assert.Equal(NewEmail, c.User.Email);
        Assert.Equal(AtsRole.Viewer, c.User.Role);
    }

    [Fact]
    public async Task Confirming_sets_the_email_rotates_the_stamp_and_audits()
    {
        var c = new Ctx();
        var before = c.User.SecurityStamp;
        var token = c.TokenFor(c.User);
        Assert.Equal(NewEmail, await c.Service.ReadUsableAsync(token));

        var result = await c.Service.ConfirmAsync(token);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(NewEmail, c.User.Email);
        Assert.NotEqual(before, c.User.SecurityStamp);
        var (user, action, summary) = Assert.Single(c.Store.Audits);
        Assert.Same(c.User, user);
        Assert.Equal("UserEmailChanged", action);
        Assert.Contains("ada@acme.test", summary);
        Assert.Contains(NewEmail, summary);
        Assert.Equal(1, c.Store.SaveCount);
    }

    [Fact]
    public async Task Viewing_the_link_changes_nothing()
    {
        var c = new Ctx();
        var before = c.User.SecurityStamp;
        Assert.Equal(NewEmail, await c.Service.ReadUsableAsync(c.TokenFor(c.User)));
        AssertNothingChanged(c);
        Assert.Equal(before, c.User.SecurityStamp);
    }

    [Fact]
    public async Task The_same_link_fails_the_second_time()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.True((await c.Service.ConfirmAsync(token)).Succeeded);

        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        Assert.Single(c.Store.Audits);
    }

    [Fact]
    public async Task Confirming_after_someone_else_took_the_address_fails()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.Store.TakenEmails.Add(NewEmail);
        Assert.Equal("That email address is already registered.", (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Confirming_when_the_unique_index_rejects_the_address_fails_with_the_same_message()
    {
        var c = new Ctx();
        c.Store.RejectNextSaveAsDuplicate = true;
        Assert.Equal("That email address is already registered.", (await c.Service.ConfirmAsync(c.TokenFor(c.User))).Error);
    }

    [Fact]
    public async Task A_token_with_a_stale_stamp_fails()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.User.SecurityStamp = Guid.NewGuid();
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Confirming_fails_when_the_user_was_deactivated()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.User.IsActive = false;
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    // An invite-pending user's address changes at once through UpdateAsync, never by link.
    [Fact]
    public async Task A_valid_link_fails_for_an_invite_pending_user()
    {
        var c = new Ctx(passwordHash: "");
        var token = c.TokenFor(c.User);
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Confirming_fails_when_the_tenant_was_suspended()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.Store.SuspendedTenants.Add(TenantId);
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("forged")]
    public async Task An_unreadable_token_fails(string? token)
    {
        var c = new Ctx();
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task A_token_of_another_purpose_or_without_an_address_fails()
    {
        var c = new Ctx();
        var invite = c.Tokens.Create(AccountTokenPurpose.Invite, new AccountToken(c.User.Id, TenantId, c.User.SecurityStamp, NewEmail));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(invite)).Error);
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(c.TokenFor(c.User, null))).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task A_token_naming_another_tenant_finds_no_user()
    {
        var c = new Ctx();
        var token = c.Tokens.Create(AccountTokenPurpose.EmailChange, new AccountToken(c.User.Id, TenantId + 1, c.User.SecurityStamp, NewEmail));
        Assert.Equal(EmailChangeService.InvalidLink, (await c.Service.ConfirmAsync(token)).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task A_pending_users_email_change_applies_at_once_and_the_invite_goes_to_the_new_address()
    {
        var c = new Ctx(passwordHash: "");
        var update = await c.Users.UpdateAsync(new UpdateUserInput(c.User.Id, "Ada", NewEmail, AtsRole.Recruiter), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Null(update.PendingEmail);
        Assert.Equal(NewEmail, c.User.Email);

        Assert.True((await c.Invites.SendInviteAsync(c.User.Id)).Succeeded);
        var (draft, _) = Assert.Single(c.Outbox.Staged);
        Assert.Equal(NewEmail, draft.To);
        Assert.Contains("/Account/AcceptInvite?token=", draft.TextBody);
    }

    private static void AssertNothingChanged(Ctx c)
    {
        Assert.Equal("ada@acme.test", c.User.Email);
        Assert.Empty(c.Store.Audits);
        Assert.Equal(0, c.Store.SaveCount);
    }
}
