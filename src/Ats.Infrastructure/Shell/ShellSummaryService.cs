using Ats.Application.Abstractions;
using Ats.Application.Shell;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Shell;

// Registered scoped: _cached makes this one batch per request. Every count is tenant-scoped
// automatically by the global query filter, so none of them needs a TenantId predicate.
public sealed class ShellSummaryService : IShellSummaryService
{
    private const int IdleDays = 7;
    private const int StaleDraftDays = 7;

    private readonly AtsDbContext _db;
    private readonly ITenantContext _tenant;
    private ShellSummary? _cached;

    public ShellSummaryService(AtsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<ShellSummary> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        if (!_tenant.HasTenant) return _cached = ShellSummary.Empty;

        var now = DateTimeOffset.UtcNow;
        var idleBefore = now.AddDays(-IdleDays);
        var draftBefore = now.AddDays(-StaleDraftDays);

        var openJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);
        var candidates = await _db.Candidates.CountAsync(ct);
        var failed = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Failed, ct);

        // Idle: active, and nothing has happened since idleBefore. An application with no events
        // falls back to its AppliedAt.
        var idle = await _db.Applications
            .Where(a => a.Status == ApplicationStatus.Active)
            .Select(a => _db.ApplicationEvents
                .Where(e => e.ApplicationId == a.Id)
                .Max(e => (DateTimeOffset?)e.OccurredAt) ?? a.AppliedAt)
            .CountAsync(lastActivity => lastActivity < idleBefore, ct);

        var staleDrafts = await _db.Jobs
            .CountAsync(j => j.Status == JobStatus.Draft && j.CreatedAt < draftBefore, ct);

        return _cached = new ShellSummary(openJobs, candidates, failed, idle, staleDrafts);
    }
}
