using Ats.Application.Candidates;
using Ats.Application.Common;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Candidates;

// Read projection for the candidates list. Paginates candidates on the server, then enriches the
// visible page from their applications (latest source/job/stage, count, last activity).
public sealed class CandidateListQuery : ICandidateListQuery
{
    private readonly AtsDbContext _db;
    public CandidateListQuery(AtsDbContext db) => _db = db;

    public async Task<PagedResult<CandidateListItem>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Candidates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => EF.Functions.Like(c.FirstName, $"%{s}%")
                          || EF.Functions.Like(c.LastName, $"%{s}%")
                          || EF.Functions.Like(c.Email, $"%{s}%"));
        }

        var total = await q.CountAsync(ct);
        var candidates = await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.Phone })
            .ToListAsync(ct);

        var ids = candidates.Select(c => c.Id).ToList();

        var apps = await (
            from a in _db.Applications
            where ids.Contains(a.CandidateId)
            join j in _db.Jobs on a.JobId equals j.Id
            join st in _db.PipelineStages on a.CurrentStageId equals st.Id
            select new
            {
                a.CandidateId, a.Id, a.AppliedAt, a.Origin,
                JobTitle = j.Title, StageName = st.Name,
                LastEvent = _db.ApplicationEvents.Where(e => e.ApplicationId == a.Id).Max(e => (DateTimeOffset?)e.OccurredAt)
            })
            .ToListAsync(ct);

        var byCandidate = apps.GroupBy(a => a.CandidateId).ToDictionary(g => g.Key, g => g.ToList());

        var items = candidates.Select(c =>
        {
            var list = byCandidate.TryGetValue(c.Id, out var l) ? l : new();
            var latest = list.OrderByDescending(a => a.AppliedAt).FirstOrDefault();
            var lastActivity = list
                .Select(a => a.LastEvent ?? a.AppliedAt)
                .DefaultIfEmpty()
                .Max();
            return new CandidateListItem(
                c.Id, c.FirstName + " " + c.LastName, c.Email, c.Phone,
                latest?.Origin ?? Ats.Domain.Enums.ApplicationOrigin.Unknown,
                latest?.JobTitle, latest?.StageName,
                list.Count,
                list.Count == 0 ? null : lastActivity);
        }).ToList();

        return new PagedResult<CandidateListItem>(items, page, pageSize, total);
    }
}
