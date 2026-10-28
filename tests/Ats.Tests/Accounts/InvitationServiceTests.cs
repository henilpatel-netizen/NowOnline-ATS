using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Accounts;

// An invite link sets the first password of an account and signs its holder in, signed out and across
// tenants: a wrong rule lets a dead link (resent, deactivated, used) take over the account.
public class InvitationServiceTests
{
    private const string BaseUrl = "https://ats.example.test/";
    private const int TenantId = 4;

    private sealed class Ctx
    {
        public FakeUserRepository Repo { get; } = new();
        public FakeAccountStore Store { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeEmailOutbox Outbox { get; } = new();
        public InvitationService Service { get; }
        public AppUser User { get; }

        public Ctx()
        {
            User = new AppUser
            {
                Id = 12,
                TenantId = TenantId,
                Email = "ada@acme.test",
                DisplayName = "Ada",
                Role = AtsRole.Recruiter,
                PasswordHash = "",
                IsActive = true
            };
            // The same tracked user, reached through the tenant-filtered repository (Owner signed in) and
            // through the account store (signed-out link).
            Repo.Users.Add(User);
            Store.Users.Add(User);
            Service = new InvitationService(Repo, Store, Tokens, Outbox, new FakeIdentityService(), new FakeTenantBranding(),
                new FakeCurrentUser { Name = "Olivia Owner" }, new IntegrationOptions { CareerSiteBaseUrl = BaseUrl });
        }

        public string TokenFor(AppUser u) => Tokens.Create(AccountTokenPurpose.Invite, new AccountToken(u.Id, u.TenantId, u.SecurityStamp));

        public string LinkToken(int index = 0) =>
            Uri.UnescapeDataString(Outbox.Staged[index].Draft.TextBody.Split("?token=")[1].Split(['\r', '\n', ' '])[0]);
    }

    [Fact]
    public async Task Sending_stages_one_invite_to_the_new_address_with_an_accept_link_and_the_inviter()
    {
        var c = new Ctx();
        var result = await c.Service.SendInviteAsync(c.User.Id);

        Assert.True(result.Succeeded, result.Error);
        var (draft, tenantId) = Assert.Single(c.Outbox.Staged);
        Assert.Equal("ada@acme.test", draft.To);
        Assert.Equal(TenantId, tenantId);
        Assert.Contains("https://ats.example.test/Account/AcceptInvite?token=", draft.TextBody);
        Assert.Contains("Olivia Owner", draft.TextBody);
        Assert.Equal(1, c.Repo.SaveCount);
    }

    [Fact]
    public async Task The_invite_link_reads_back_to_the_user()
    {
        var c = new Ctx();
        await c.Service.SendInviteAsync(c.User.Id);
        var details = await c.Service.ReadUsableAsync(c.LinkToken());
        Assert.NotNull(details);
        Assert.Equal("Ada", details.DisplayName);
        Assert.Equal("ada@acme.test", details.Email);
    }

    [Fact]
    public async Task Sending_to_a_user_who_already_has_a_password_fails()
    {
        var c = new Ctx();
        c.User.PasswordHash = "hash:x";
        Assert.Equal(InvitationService.AlreadyAccepted, (await c.Service.SendInviteAsync(c.User.Id)).Error);
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task Sending_to_an_unknown_user_fails()
    {
        var c = new Ctx();
        Assert.False((await c.Service.SendInviteAsync(99)).Succeeded);
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task Resending_to_a_user_who_already_accepted_fails()
    {
        var c = new Ctx();
        c.User.PasswordHash = "hash:x";
        var stamp = c.User.SecurityStamp;
        Assert.Equal("This user has already accepted.", (await c.Service.ResendAsync(c.User.Id)).Error);
        Assert.Empty(c.Outbox.Staged);
        Assert.Equal(stamp, c.User.SecurityStamp);
        Assert.Equal(0, c.Repo.SaveCount);
    }

    [Fact]
    public async Task Resending_to_a_deactivated_user_fails()
    {
        var c = new Ctx();
        c.User.IsActive = false;
        Assert.False((await c.Service.ResendAsync(c.User.Id)).Succeeded);
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task Resending_rotates_the_stamp_so_the_first_link_no_longer_accepts()
    {
        var c = new Ctx();
        await c.Service.SendInviteAsync(c.User.Id);
        var first = c.LinkToken(0);

        Assert.True((await c.Service.ResendAsync(c.User.Id)).Succeeded);
        Assert.Equal(2, c.Outbox.Staged.Count);
        var second = c.LinkToken(1);

        Assert.Null(await c.Service.ReadUsableAsync(first));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(first, "a-new-password")).Error);
        Assert.Equal("", c.User.PasswordHash);
        Assert.True((await c.Service.AcceptAsync(second, "a-new-password")).Succeeded);
    }

    [Fact]
    public async Task Accepting_sets_the_password_rotates_the_stamp_audits_and_signs_in()
    {
        var c = new Ctx();
        var before = c.User.SecurityStamp;
        var result = await c.Service.AcceptAsync(c.TokenFor(c.User), "a-new-password");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("hash:a-new-password", c.User.PasswordHash);
        Assert.NotEqual(before, c.User.SecurityStamp);
        Assert.False(c.User.MustChangePassword);
        Assert.Equal(c.User.Id, result.UserId);
        Assert.Equal(TenantId, result.TenantId);
        Assert.Equal(AtsRole.Recruiter, result.Role);
        Assert.Equal("Ada", result.DisplayName);
        Assert.Equal(c.User.SecurityStamp, result.SecurityStamp);
        Assert.False(result.MustChangePassword);
        var (user, action, summary) = Assert.Single(c.Store.Audits);
        Assert.Same(c.User, user);
        Assert.Equal("UserInviteAccepted", action);
        Assert.DoesNotContain("a-new-password", summary);
        Assert.Equal(1, c.Store.SaveCount);
    }

    [Fact]
    public async Task The_same_link_fails_the_second_time()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.True((await c.Service.AcceptAsync(token, "a-new-password")).Succeeded);

        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(token, "another-password")).Error);
        Assert.Equal("hash:a-new-password", c.User.PasswordHash);
    }

    // A fresh token for a user who already accepted (a stamp that was never rotated) must not reset the password.
    [Fact]
    public async Task A_valid_token_for_a_user_who_already_has_a_password_fails()
    {
        var c = new Ctx();
        c.User.PasswordHash = "hash:old-password";
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(c.TokenFor(c.User), "a-new-password")).Error);
        AssertNothingChanged(c, "hash:old-password");
    }

    [Fact]
    public async Task A_token_with_a_stale_stamp_fails()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.User.SecurityStamp = Guid.NewGuid();
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Accepting_fails_when_the_user_was_deactivated_after_the_invite()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.User.IsActive = false;
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Accepting_fails_when_the_tenant_was_suspended()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.Store.SuspendedTenants.Add(TenantId);
        Assert.Null(await c.Service.ReadUsableAsync(token));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(token, "a-new-password")).Error);
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
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task A_password_reset_token_does_not_accept_an_invite()
    {
        var c = new Ctx();
        var reset = c.Tokens.Create(AccountTokenPurpose.PasswordReset, new AccountToken(c.User.Id, TenantId, c.User.SecurityStamp));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(reset, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task A_token_naming_another_tenant_finds_no_user()
    {
        var c = new Ctx();
        var token = c.Tokens.Create(AccountTokenPurpose.Invite, new AccountToken(c.User.Id, TenantId + 1, c.User.SecurityStamp));
        Assert.Equal(InvitationService.InvalidInvite, (await c.Service.AcceptAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task An_over_long_password_fails_and_leaves_the_link_usable()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.Equal("Password must be 128 characters or fewer.", (await c.Service.AcceptAsync(token, new string('p', 129))).Error);
        AssertNothingChanged(c);
        Assert.NotNull(await c.Service.ReadUsableAsync(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_password_fails_and_leaves_the_link_usable(string password)
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.Equal("Enter a password.", (await c.Service.AcceptAsync(token, password)).Error);
        AssertNothingChanged(c);
        Assert.NotNull(await c.Service.ReadUsableAsync(token));
    }

    private static void AssertNothingChanged(Ctx c, string hash = "")
    {
        Assert.Equal(hash, c.User.PasswordHash);
        Assert.Empty(c.Store.Audits);
        Assert.Equal(0, c.Store.SaveCount);
    }
}
