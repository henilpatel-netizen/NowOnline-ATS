using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Accounts;

// Forgot password runs signed out and across tenants: a wrong rule leaks which addresses exist, mails a
// link on an attacker's host, or lets a link be used twice.
public class AccountRecoveryServiceTests
{
    private const string BaseUrl = "https://ats.example.test/";
    private const int TenantId = 4;

    private sealed class Ctx
    {
        public FakeAccountStore Store { get; } = new();
        public FakeUserRepository Repo { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeEmailOutbox Outbox { get; } = new();
        public AccountRecoveryService Service { get; }
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
                PasswordHash = "hash:old-password",
                IsActive = true,
                MustChangePassword = true
            };
            // The same tracked user, reached through the account store (signed-out link) and through the
            // tenant-filtered repository (Owner signed in).
            Store.Users.Add(User);
            Repo.Users.Add(User);
            Service = new AccountRecoveryService(Store, Tokens, Outbox, new FakeIdentityService(), Repo, new FakeTenantBranding(),
                new IntegrationOptions { CareerSiteBaseUrl = BaseUrl });
        }

        public string TokenFor(AppUser u) => Tokens.Create(AccountTokenPurpose.PasswordReset, new AccountToken(u.Id, u.TenantId, u.SecurityStamp));
    }

    [Fact]
    public async Task A_known_user_gets_one_reset_link_on_the_configured_base_url()
    {
        var c = new Ctx();
        await c.Service.RequestResetAsync("  ADA@acme.test ");
        var (draft, tenantId) = Assert.Single(c.Outbox.Staged);
        Assert.Equal("ada@acme.test", draft.To);
        Assert.Equal(TenantId, tenantId);
        Assert.Contains("https://ats.example.test/Account/ResetPassword?token=", draft.TextBody);
        Assert.Equal(1, c.Outbox.SendCount);
    }

    [Fact]
    public async Task The_link_carries_a_token_that_reads_back_to_the_user()
    {
        var c = new Ctx();
        await c.Service.RequestResetAsync("ada@acme.test");
        var token = Uri.UnescapeDataString(c.Outbox.Staged[0].Draft.TextBody.Split("?token=")[1].Split(['\r', '\n', ' '])[0]);
        Assert.True(await c.Service.IsResetTokenUsableAsync(token));
    }

    [Fact]
    public async Task An_unknown_email_stages_nothing()
    {
        var c = new Ctx();
        await c.Service.RequestResetAsync("nobody@acme.test");
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task An_inactive_user_gets_nothing()
    {
        var c = new Ctx();
        c.User.IsActive = false;
        await c.Service.RequestResetAsync("ada@acme.test");
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task An_invite_pending_user_gets_nothing()
    {
        var c = new Ctx();
        c.User.PasswordHash = "";
        await c.Service.RequestResetAsync("ada@acme.test");
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task A_user_of_a_suspended_tenant_gets_nothing()
    {
        var c = new Ctx();
        c.Store.SuspendedTenants.Add(TenantId);
        await c.Service.RequestResetAsync("ada@acme.test");
        Assert.Empty(c.Outbox.Staged);
    }

    [Fact]
    public async Task Reset_sets_the_password_rotates_the_stamp_audits_and_notifies_in_one_save()
    {
        var c = new Ctx();
        var before = c.User.SecurityStamp;
        var result = await c.Service.ResetAsync(c.TokenFor(c.User), "a-new-password");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("hash:a-new-password", c.User.PasswordHash);
        Assert.False(c.User.MustChangePassword);
        Assert.NotEqual(before, c.User.SecurityStamp);
        var (user, action, summary) = Assert.Single(c.Store.Audits);
        Assert.Same(c.User, user);
        Assert.Equal("UserPasswordReset", action);
        Assert.Equal("Password reset by email link", summary);
        var (draft, tenantId) = Assert.Single(c.Outbox.Staged);
        Assert.Equal("Your password for Acme was changed", draft.Subject);
        Assert.Equal("ada@acme.test", draft.To);
        Assert.Equal(TenantId, tenantId);
        Assert.Equal(0, c.Outbox.SendCount);
        Assert.Equal(1, c.Store.SaveCount);
    }

    [Fact]
    public async Task The_same_link_fails_the_second_time()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.True((await c.Service.ResetAsync(token, "a-new-password")).Succeeded);

        Assert.False(await c.Service.IsResetTokenUsableAsync(token));
        var second = await c.Service.ResetAsync(token, "another-password");
        Assert.Equal(AccountRecoveryService.InvalidLink, second.Error);
        Assert.Equal("hash:a-new-password", c.User.PasswordHash);
    }

    [Fact]
    public async Task A_token_with_a_stale_stamp_fails()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.User.SecurityStamp = Guid.NewGuid();
        Assert.False(await c.Service.IsResetTokenUsableAsync(token));
        Assert.Equal(AccountRecoveryService.InvalidLink, (await c.Service.ResetAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("forged")]
    public async Task An_unreadable_token_fails(string? token)
    {
        var c = new Ctx();
        Assert.False(await c.Service.IsResetTokenUsableAsync(token));
        Assert.Equal(AccountRecoveryService.InvalidLink, (await c.Service.ResetAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task A_token_for_another_purpose_fails()
    {
        var c = new Ctx();
        var invite = c.Tokens.Create(AccountTokenPurpose.Invite, new AccountToken(c.User.Id, TenantId, c.User.SecurityStamp));
        Assert.Equal(AccountRecoveryService.InvalidLink, (await c.Service.ResetAsync(invite, "a-new-password")).Error);
    }

    [Fact]
    public async Task A_token_naming_another_tenant_finds_no_user()
    {
        var c = new Ctx();
        var token = c.Tokens.Create(AccountTokenPurpose.PasswordReset, new AccountToken(c.User.Id, TenantId + 1, c.User.SecurityStamp));
        Assert.Equal(AccountRecoveryService.InvalidLink, (await c.Service.ResetAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Reset_fails_when_the_user_was_deactivated_after_the_link_was_sent()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.User.IsActive = false;
        Assert.Equal(AccountRecoveryService.InvalidLink, (await c.Service.ResetAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Fact]
    public async Task Reset_fails_when_the_tenant_was_suspended_after_the_link_was_sent()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        c.Store.SuspendedTenants.Add(TenantId);
        Assert.False(await c.Service.IsResetTokenUsableAsync(token));
        Assert.Equal(AccountRecoveryService.InvalidLink, (await c.Service.ResetAsync(token, "a-new-password")).Error);
        AssertNothingChanged(c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_password_fails_and_leaves_the_link_usable(string password)
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.Equal("Enter a password.", (await c.Service.ResetAsync(token, password)).Error);
        AssertNothingChanged(c);
        Assert.True(await c.Service.IsResetTokenUsableAsync(token));
    }

    [Fact]
    public async Task An_over_long_password_fails_and_leaves_the_link_usable()
    {
        var c = new Ctx();
        var token = c.TokenFor(c.User);
        Assert.Equal("Password must be 128 characters or fewer.", (await c.Service.ResetAsync(token, new string('p', 129))).Error);
        AssertNothingChanged(c);
        Assert.True(await c.Service.IsResetTokenUsableAsync(token));
    }

    private const int OwnerId = 1;

    [Fact]
    public async Task An_Owner_sends_one_reset_link_and_changes_nothing_else()
    {
        var c = new Ctx();
        var (hash, stamp) = (c.User.PasswordHash, c.User.SecurityStamp);
        var result = await c.Service.SendResetLinkAsync(c.User.Id, OwnerId);

        Assert.True(result.Succeeded, result.Error);
        var (draft, tenantId) = Assert.Single(c.Outbox.Staged);
        Assert.Equal("ada@acme.test", draft.To);
        Assert.Equal("Reset your password for Acme", draft.Subject);
        Assert.Equal(TenantId, tenantId);
        Assert.Contains("https://ats.example.test/Account/ResetPassword?token=", draft.TextBody);
        Assert.Equal(hash, c.User.PasswordHash);
        Assert.Equal(stamp, c.User.SecurityStamp);
        Assert.True(c.User.MustChangePassword);
    }

    [Fact]
    public async Task An_Owner_cannot_send_a_reset_link_to_themselves()
    {
        var c = new Ctx();
        AssertRefused(c, await c.Service.SendResetLinkAsync(c.User.Id, c.User.Id), "Use Change password for your own account.");
    }

    [Fact]
    public async Task No_reset_link_for_a_deactivated_user()
    {
        var c = new Ctx();
        c.User.IsActive = false;
        AssertRefused(c, await c.Service.SendResetLinkAsync(c.User.Id, OwnerId), "Reactivate the user first.");
    }

    [Fact]
    public async Task No_reset_link_for_an_invite_pending_user()
    {
        var c = new Ctx();
        c.User.PasswordHash = "";
        AssertRefused(c, await c.Service.SendResetLinkAsync(c.User.Id, OwnerId),
            "This user has not accepted their invitation yet. Use Resend invite.");
    }

    // The repository is tenant-filtered, so a user of another tenant is simply not found.
    [Fact]
    public async Task No_reset_link_for_an_unknown_user()
    {
        var c = new Ctx();
        AssertRefused(c, await c.Service.SendResetLinkAsync(99, OwnerId), "User not found.");
    }

    private static void AssertRefused(Ctx c, OperationResult result, string error)
    {
        Assert.Equal(error, result.Error);
        Assert.Empty(c.Outbox.Staged);
        Assert.Equal(0, c.Repo.SaveCount);
    }

    private static void AssertNothingChanged(Ctx c)
    {
        Assert.Equal("hash:old-password", c.User.PasswordHash);
        Assert.Empty(c.Store.Audits);
        Assert.Empty(c.Outbox.Staged);
        Assert.Equal(0, c.Store.SaveCount);
    }
}
