using Ats.Application.Common;
using Ats.Application.Jobs;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Jobs;

// Read projection for the jobs list. Filters/paginates on the server, then enriches only the
// visible page (at most pageSize jobs) with counts and the avatar-stack names.
public sealed class JobListQuery : IJobListQuery
{
    private readonly AtsDbContext _db;
    public JobListQuery(AtsDbContext db) => _db = db;

    public async Task<PagedResult<JobListItem>> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Jobs.AsQueryable();
        if (status is not null) q = q.Where(j => j.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(j => EF.Functions.Like(j.Title, $"%{s}%") || EF.Functions.Like(j.ExternalRef, $"%{s}%"));
        }

        var total = await q.CountAsync(ct);

        var jobs = await q
            .OrderByDescending(j => j.PublishedAt).ThenByDescending(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.ExternalRef,
                j.Status,
                j.PublishedAt,
                Department = j.Department != null ? j.Department.Name : null,
                Location = j.Location != null ? (j.Location.City ?? j.Location.Name) : null
            })
            .ToListAsync(ct);

        var ids = jobs.Select(j => j.Id).ToList();

        // Stage tallies are grouped in SQL, so only one row per (job, stage) comes back instead of
        // one row per active application.
        var stageRows = await (
            from a in _db.Applications
            where ids.Contains(a.JobId) && a.Status == ApplicationStatus.Active
            join st in _db.PipelineStages on a.CurrentStageId equals st.Id
            group a by new { a.JobId, st.Name, st.Order } into g
            select new { g.Key.JobId, g.Key.Name, g.Key.Order, Count = g.Count() })
            .ToListAsync(ct);

        var totalByJob = await _db.Applications
            .Where(a => ids.Contains(a.JobId))
            .GroupBy(a => a.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobId, x => x.Count, ct);

        // Only the three avatar names per job are fetched (a correlated TOP 3), so the cost no longer
        // grows with how many people applied to a job.
        var nameRows = await _db.Jobs
            .Where(j => ids.Contains(j.Id))
            .Select(j => new
            {
                JobId = j.Id,
                Names = _db.Applications
                    .Where(a => a.JobId == j.Id)
                    .OrderBy(a => a.Id)
                    .Take(3)
                    // CandidateId is a required FK (Restrict delete), so the row always exists and
                    // this is translated to a SQL join, never dereferenced in memory.
                    .Select(a => a.Candidate!.FirstName + " " + a.Candidate!.LastName)
                    .ToList()
            })
            .ToListAsync(ct);
        var names = nameRows.ToDictionary(x => x.JobId, x => x.Names);

        var stagesByJob = stageRows.GroupBy(r => r.JobId).ToDictionary(g => g.Key, g => g.ToList());

        var items = jobs.Select(j =>
        {
            var rows = stagesByJob.TryGetValue(j.Id, out var list) ? list : new();
            var stageCounts = rows
                .OrderBy(r => r.Order)
                .Select(r => new JobStageCount(r.Name, r.Count))
                .ToList();
            return new JobListItem(
                j.Id, j.Title, j.ExternalRef, j.Status, j.Department, j.Location, j.PublishedAt,
                totalByJob.TryGetValue(j.Id, out var t) ? t : 0,
                rows.Sum(r => r.Count),
                stageCounts,
                names.TryGetValue(j.Id, out var n) ? n : new List<string>());
        }).ToList();

        return new PagedResult<JobListItem>(items, page, pageSize, total);
    }
}
