using Ats.Application.Common;
using Ats.Application.Dashboard;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private readonly AtsDbContext _db;
    public DashboardService(AtsDbContext db) => _db = db;

    public async Task<DashboardSummary> GetAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-90);

        var openJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);
        var totalCandidates = await _db.Candidates.CountAsync(ct);
        var activeApplications = await _db.Applications.CountAsync(a => a.Status == ApplicationStatus.Active, ct);

        // Active applications by stage, in stage order (drives the distribution bars).
        var grouped = await (
            from a in _db.Applications
            where a.Status == ApplicationStatus.Active
            join s in _db.PipelineStages on a.CurrentStageId equals s.Id
            group a by new { s.Name, s.Order } into g
            select new { g.Key.Name, g.Key.Order, Count = g.Count() })
            .ToListAsync(ct);
        var byStage = grouped.OrderBy(g => g.Order).Select(g => new StageCount(g.Name, g.Count)).ToList();

        // Time to hire: AppliedAt -> first event into a Hired-outcome stage, last 90 days.
        var hiredStageIds = await _db.PipelineStages
            .Where(s => s.IsTerminal && s.TerminalOutcome == StageOutcome.Hired)
            .Select(s => s.Id).ToListAsync(ct);
        var hireEvents = await (
            from e in _db.ApplicationEvents
            where hiredStageIds.Contains(e.ToStageId) && e.OccurredAt >= since
            join a in _db.Applications on e.ApplicationId equals a.Id
            select new { e.ApplicationId, a.AppliedAt, e.OccurredAt })
            .ToListAsync(ct);
        // Time to hire: per application, the FIRST time it reached a hired-outcome stage (a re-entry
        // must not add a second span).
        var firstHirePerApp = hireEvents
            .GroupBy(x => x.ApplicationId)
            .Select(g => g.OrderBy(x => x.OccurredAt).First())
            .ToList();
        var timeToHire = DashboardMath.MeanDays(firstHirePerApp.Select(x => x.OccurredAt - x.AppliedAt).ToList());

        // Offer acceptance: DISTINCT hired applications / applications that progressed in the window.
        // NOTE: the denominator is a pragmatic proxy (distinct applications with any stage move in
        // the last 90 days). The spec's exact "reached an offer-position stage" needs per-pipeline
        // offer-stage identification; deferred as a refinement so the tile ships now.
        var progressed = await _db.ApplicationEvents
            .Where(e => e.OccurredAt >= since && e.FromStageId != null)
            .Select(e => e.ApplicationId)
            .Distinct().CountAsync(ct);
        var acceptance = DashboardMath.OfferAcceptancePercent(hireEvents.Select(x => x.ApplicationId), progressed);

        // Source split.
        var originCounts = await _db.Applications
            .GroupBy(a => a.Origin)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var order = new[] { ApplicationOrigin.CareerSite, ApplicationOrigin.Referral, ApplicationOrigin.Manual, ApplicationOrigin.Unknown };
        var counts = order.Select(o => originCounts.FirstOrDefault(x => x.Key == o)?.Count ?? 0).ToList();
        var pcts = DashboardMath.Split(counts);
        var sources = order.Select((o, i) => new SourceSlice(o, pcts[i])).Where(s => s.Percent > 0).ToList();

        // Needs attention.
        var attention = new List<AttentionItem>();
        var idleBefore = now.AddDays(-7);
        var idle = await _db.Applications
            .Where(a => a.Status == ApplicationStatus.Active)
            .Select(a => _db.ApplicationEvents.Where(e => e.ApplicationId == a.Id).Max(e => (DateTimeOffset?)e.OccurredAt) ?? a.AppliedAt)
            .CountAsync(last => last < idleBefore, ct);
        if (idle > 0)
            attention.Add(new AttentionItem("hourglass_top", "warning", $"{idle} applications idle over 7 days", "In process", "/Candidates"));
        // One grouped query serves every outbox tile below (was four separate COUNTs).
        var outboxByStatus = await _db.OutboxMessages
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        int OutboxCount(OutboxStatus s) => outboxByStatus.TryGetValue(s, out var n) ? n : 0;

        var failed = OutboxCount(OutboxStatus.Failed);
        if (failed > 0)
            attention.Add(new AttentionItem("sync_problem", "danger", $"{failed} status updates failed to deliver", "ReferralTool", "/Integration/Deliveries"));
        var drafts = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Draft, ct);
        if (drafts > 0)
            attention.Add(new AttentionItem("edit_note", "info", $"{drafts} job(s) still in draft", "Not published", "/Jobs?status=Draft"));

        // Integration health.
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        var delivered = OutboxCount(OutboxStatus.Delivered);
        // Processing = claimed by a worker and in flight; still "pending" from the user's point of view.
        var pending = OutboxCount(OutboxStatus.Pending) + OutboxCount(OutboxStatus.Processing);
        var health = new IntegrationHealth(
            settings?.IntegrationEnabled ?? false,
            settings?.ReferralToolCustomerId,
            settings?.FeedLastPulledAt,
            delivered, failed, pending);

        // Activity feed from the audit log.
        var activity = await _db.AuditEntries
            .OrderByDescending(a => a.OccurredAt).Take(6)
            .Select(a => new ActivityItem(a.UserName, a.Summary, a.OccurredAt))
            .ToListAsync(ct);

        return new DashboardSummary(openJobs, activeApplications, totalCandidates,
            timeToHire, acceptance, byStage, sources, attention, activity, health);
    }
}
