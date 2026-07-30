using Ats.Application.Auditing;
using Ats.Application.Common;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Auditing;

public sealed class AuditQuery : IAuditQuery
{
    private readonly AtsDbContext _db;
    public AuditQuery(AtsDbContext db) => _db = db;

    public Task<List<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default) =>
        _db.AuditEntries.OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);

    public async Task<PagedResult<AuditEntry>> SearchAsync(string? q, string? action, DateTimeOffset? from, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(a => EF.Functions.Like(a.UserName, $"%{s}%")
                                  || EF.Functions.Like(a.Summary, $"%{s}%")
                                  || (a.EntityRef != null && EF.Functions.Like(a.EntityRef, $"%{s}%")));
        }
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (from is not null) query = query.Where(a => a.OccurredAt >= from);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditEntry>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default) =>
        await _db.AuditEntries.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
}
