using Ats.Application.Abstractions;
using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Xunit;

namespace Ats.Tests.Tenancy;

public class TenantOnboardingServiceTests
{
    // Email is globally unique across tenants (SEC-7): registration must reject an address that is
    // already in use, before attempting the insert.
    [Fact]
    public async Task Rejects_an_email_that_is_already_registered()
    {
        var store = new FakeStore { EmailTaken = true };
        var service = new TenantOnboardingService(store, new FakeIdentity());

        var result = await service.RegisterAsync(Input("acme", "owner@acme.test"));

        Assert.False(result.Succeeded);
        Assert.Equal("That email address is already registered.", result.Error);
        Assert.False(store.CreateCalled);
    }

    [Fact]
    public async Task Normalises_the_email_before_the_uniqueness_check()
    {
        var store = new FakeStore { EmailTaken = false };
        var service = new TenantOnboardingService(store, new FakeIdentity());

        await service.RegisterAsync(Input("acme", "  Owner@ACME.test  "));

        Assert.Equal("owner@acme.test", store.CheckedEmail);
        Assert.Equal("owner@acme.test", store.CreatedOwnerEmail);
    }

    [Fact]
    public async Task Creates_the_tenant_when_slug_and_email_are_free()
    {
        var store = new FakeStore();
        var service = new TenantOnboardingService(store, new FakeIdentity());

        var result = await service.RegisterAsync(Input("acme", "owner@acme.test"));

        Assert.True(result.Succeeded);
        Assert.True(store.CreateCalled);
    }

    [Fact]
    public async Task Rejects_a_taken_slug_without_checking_email()
    {
        var store = new FakeStore { SlugTaken = true };
        var service = new TenantOnboardingService(store, new FakeIdentity());

        var result = await service.RegisterAsync(Input("acme", "owner@acme.test"));

        Assert.False(result.Succeeded);
        Assert.Equal("That URL slug is already taken.", result.Error);
        Assert.Null(store.CheckedEmail);
    }

    private static RegisterTenantInput Input(string slug, string email) =>
        new("Acme Ltd", slug, "Owner", email, "correct horse battery staple");

    private sealed class FakeStore : IOnboardingStore
    {
        public bool SlugTaken { get; init; }
        public bool EmailTaken { get; init; }
        public string? CheckedEmail { get; private set; }
        public string? CreatedOwnerEmail { get; private set; }
        public bool CreateCalled { get; private set; }

        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => Task.FromResult(SlugTaken);

        public Task<bool> EmailExistsAsync(string email, CancellationToken ct)
        {
            CheckedEmail = email;
            return Task.FromResult(EmailTaken);
        }

        public Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(
            Tenant tenant, TenantSettings settings, PipelineTemplate template,
            string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct)
        {
            CreateCalled = true;
            CreatedOwnerEmail = ownerEmail;
            return Task.FromResult((1, 1));
        }
    }

    private sealed class FakeIdentity : IIdentityService
    {
        public Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default) => Task.FromResult(1);
        public Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) => Task.FromResult(new SignInResult(true, 1, 1, "Owner", "Owner", null));
        public string HashPassword(string password) => "hash:" + password;
        public bool VerifyPassword(string hash, string password) => hash == "hash:" + password;
    }
}
