using Ats.Application.Integration;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxClaimStore : IOutboxClaimStore
{
    private readonly AtsDbContext _db;
    public OutboxClaimStore(AtsDbContext db) => _db = db;

    public Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default) =>
        _db.OutboxMessages.IgnoreQueryFilters()
            .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.ApplicationId).ThenBy(m => m.Id)
            .Take(max)
            .Select(m => new OutboxClaim(m.Id, m.TenantId, m.ApplicationId))
            .ToListAsync(ct);
}
