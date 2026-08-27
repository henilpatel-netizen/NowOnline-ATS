using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class VacancyFeedRepository : IVacancyFeedRepository
{
    private readonly AtsDbContext _db;
    public VacancyFeedRepository(AtsDbContext db) => _db = db;

    public async Task<(List<Job> Jobs, int Total)> GetPageAsync(int page, int perPage, CancellationToken ct = default)
    {
        // Draft is never exposed; the global filter already excludes soft-deleted and scopes by tenant.
        // Read-only feed projection: no change tracking needed.
        var query = _db.Jobs.AsNoTracking().Include(j => j.Location)
            .Where(j => j.Status != JobStatus.Draft)
            .OrderBy(j => j.Id);

        var total = await query.CountAsync(ct);
        var jobs = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);
        return (jobs, total);
    }

    public async Task TouchFeedPulledAsync(CancellationToken ct = default)
    {
        // Runs under the tenant set by FeedApiKeyFilter, so TenantSettings is already tenant-scoped.
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (settings is null) return;
        var now = DateTimeOffset.UtcNow;
        if (!FeedPullThrottle.ShouldRecord(settings.FeedLastPulledAt, now)) return;
        settings.FeedLastPulledAt = now;
        await _db.SaveChangesAsync(ct);
    }
}
