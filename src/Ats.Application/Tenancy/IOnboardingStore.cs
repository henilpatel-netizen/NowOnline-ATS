using Ats.Domain.Entities;

namespace Ats.Application.Tenancy;

public interface IOnboardingStore
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    // Creates Tenant + Settings + default template + owner user in one transaction.
    // Returns (tenantId, ownerUserId). Stamps TenantId explicitly on all tenant-scoped rows.
    Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(
        Tenant tenant, TenantSettings settings, PipelineTemplate template,
        string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct);
}
