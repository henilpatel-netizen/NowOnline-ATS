using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Tenancy;

public sealed class OnboardingStore : IOnboardingStore
{
    private readonly AtsDbContext _db;

    public OnboardingStore(AtsDbContext db) => _db = db;

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, ct);

    public async Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(
        Tenant tenant, TenantSettings settings, PipelineTemplate template,
        string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct)
    {
        // A retrying execution strategy (EnableRetryOnFailure) forbids user-initiated transactions
        // unless they run inside the strategy, so the whole unit of work is wrapped here and retried
        // atomically on a transient fault.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(ct);            // tenant.Id now assigned

            settings.TenantId = tenant.Id;
            template.TenantId = tenant.Id;
            foreach (var stage in template.Stages) stage.TenantId = tenant.Id;

            var owner = new AppUser
            {
                TenantId = tenant.Id,
                Email = ownerEmail,
                DisplayName = ownerName,
                PasswordHash = ownerPasswordHash,
                Role = Domain.Enums.AtsRole.Owner,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.TenantSettings.Add(settings);
            _db.PipelineTemplates.Add(template);
            _db.Users.Add(owner);
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return (tenant.Id, owner.Id);
        });
    }
}
