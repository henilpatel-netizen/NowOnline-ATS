using Ats.Application.Integration;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxClaimStore : IOutboxClaimStore
{
    private readonly AtsDbContext _db;
    private readonly IntegrationOptions _opts;

    public OutboxClaimStore(AtsDbContext db, IOptions<IntegrationOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    // Atomically claim due messages across all tenants and mark them Processing in one statement, so
    // two worker instances can never claim the same row. READPAST skips rows another worker has locked;
    // UPDLOCK/ROWLOCK take an update lock on the ones we take. We also pick up Processing rows whose
    // lease (NextAttemptAt) has expired — that reclaims messages a crashed worker left in flight.
    // The claim sets NextAttemptAt to now + ClaimLeaseSeconds as the visibility timeout.
    // Ordered oldest-first per application; the drainer preserves per-application ordering downstream.
    public async Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default)
    {
        var pending = (int)OutboxStatus.Pending;
        var processing = (int)OutboxStatus.Processing;
        var lease = now.AddSeconds(_opts.ClaimLeaseSeconds);

        // Status + NextAttemptAt must be in the CTE projection to be updatable through it.
        const string sql = @"
WITH due AS (
    SELECT TOP({0}) Id, TenantId, ApplicationId, Status, NextAttemptAt
    FROM OutboxMessages WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE Status IN ({1}, {2}) AND NextAttemptAt <= {3}
    ORDER BY ApplicationId, Id
)
UPDATE due SET Status = {2}, NextAttemptAt = {4}
OUTPUT inserted.Id, inserted.TenantId, inserted.ApplicationId;";

        return await _db.Database
            .SqlQueryRaw<OutboxClaim>(sql, max, pending, processing, now, lease)
            .ToListAsync(ct);
    }
}
