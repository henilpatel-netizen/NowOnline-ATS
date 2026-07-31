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

        // Active applications for the visible jobs, joined to stage name + order.
        var apps = await (
            from a in _db.Applications
            where ids.Contains(a.JobId) && a.Status == ApplicationStatus.Active
            join st in _db.PipelineStages on a.CurrentStageId equals st.Id
            select new { a.JobId, StageName = st.Name, st.Order })
            .ToListAsync(ct);

        var totalByJob = await _db.Applications
            .Where(a => ids.Contains(a.JobId))
            .GroupBy(a => a.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobId, x => x.Count, ct);

        // First three applicant names per visible job for the avatar stack.
        var names = (await (
            from a in _db.Applications
            where ids.Contains(a.JobId)
            join c in _db.Candidates on a.CandidateId equals c.Id
            select new { a.JobId, a.Id, Name = c.FirstName + " " + c.LastName })
            .ToListAsync(ct))
            .GroupBy(x => x.JobId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).Select(x => x.Name).Take(3).ToList());

        var byJob = apps.GroupBy(a => a.JobId).ToDictionary(g => g.Key, g => g.ToList());

        var items = jobs.Select(j =>
        {
            var jobApps = byJob.TryGetValue(j.Id, out var list) ? list : new();
            var stageCounts = jobApps
                .GroupBy(a => new { a.StageName, a.Order })
                .OrderBy(g => g.Key.Order)
                .Select(g => new JobStageCount(g.Key.StageName, g.Count()))
                .ToList();
            return new JobListItem(
                j.Id, j.Title, j.ExternalRef, j.Status, j.Department, j.Location, j.PublishedAt,
                totalByJob.TryGetValue(j.Id, out var t) ? t : 0,
                jobApps.Count,
                stageCounts,
                names.TryGetValue(j.Id, out var n) ? n : new List<string>());
        }).ToList();

        return new PagedResult<JobListItem>(items, page, pageSize, total);
    }
}
