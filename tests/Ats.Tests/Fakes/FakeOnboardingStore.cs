using Ats.Application.Tenancy;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

// Global email check only; the onboarding writes are not faked.
public sealed class FakeOnboardingStore : IOnboardingStore
{
    public bool Taken { get; set; }
    public string? Checked { get; private set; }
    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) { Checked = email; return Task.FromResult(Taken); }
    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
    public Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(Tenant tenant, TenantSettings settings,
        PipelineTemplate template, string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct) =>
        throw new NotSupportedException();
}
