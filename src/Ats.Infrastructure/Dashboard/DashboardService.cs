using Ats.Application.Dashboard;
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
        var publishedJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);
        var totalCandidates = await _db.Candidates.CountAsync(ct);
        var activeApplications = await _db.Applications.CountAsync(a => a.Status == ApplicationStatus.Active, ct);

        var grouped = await _db.Applications
            .Where(a => a.Status == ApplicationStatus.Active)
            .GroupBy(a => a.CurrentStageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var stageIds = grouped.Select(g => g.StageId).ToList();
        var stageNames = await _db.PipelineStages
            .Where(s => stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var byStage = grouped
            .Select(g => new StageCount(stageNames.TryGetValue(g.StageId, out var n) ? n : $"#{g.StageId}", g.Count))
            .OrderByDescending(s => s.Count)
            .ToList();

        var recentApps = await _db.Applications
            .Include(a => a.Candidate)
            .OrderByDescending(a => a.AppliedAt)
            .Take(5)
            .ToListAsync(ct);
        var jobIds = recentApps.Select(a => a.JobId).Distinct().ToList();
        var jobTitles = await _db.Jobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, ct);
        var recent = recentApps
            .Select(a => new RecentApplication(
                a.Candidate?.FullName ?? "(unknown)",
                jobTitles.TryGetValue(a.JobId, out var t) ? t : "(job)",
                a.AppliedAt))
            .ToList();

        return new DashboardSummary(publishedJobs, totalCandidates, activeApplications, byStage, recent);
    }
}
