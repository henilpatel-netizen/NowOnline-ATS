using Ats.Application.Auditing;
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
}
