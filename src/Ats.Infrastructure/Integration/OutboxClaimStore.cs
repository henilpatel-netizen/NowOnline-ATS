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
    // The claim sets NextAttemptAt to now + ClaimLeaseSeconds as the visibility timeout and returns it
    // as the claim's Lease (fencing token, see OutboxClaim).
    // Ordering guarantee: a message is never claimed while an older message of the same application is
    // still Pending or Processing (Delivered and Failed do not block, so a dead letter cannot freeze the
    // application). A deferred N therefore holds N+1 back however long its backoff is, and a claim
    // whose lease expired is never started (OutboxBatch) nor saved (the processor checks the lease on
    // load). Remaining window: if the lease runs out during a single in-flight call, another worker can
    // reclaim N and send it again, and a reordering is possible if that worker delivers N and N+1 before
    // the stale call lands. ReferralTool's duplicate guard rejects whichever copy of N arrives second, so
    // N cannot overwrite N+1 there. The stale worker still commits its own attempt rows (the late copy
    // shows as a rejected attempt in the delivery log), but its save to the message fails on RowVersion.
    // Cost: at most one message per application per poll cycle, so a burst of k updates for one
    // application takes about k * PollSeconds to drain.
    // The NOT EXISTS reads with READCOMMITTEDLOCK, not READPAST: an older row locked by another
    // worker must block, not be skipped, and the hint also stops RCSI (on by default in Azure SQL)
    // from reading a stale version instead of waiting for that worker's commit.
    public async Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default)
    {
        var pending = (int)OutboxStatus.Pending;
        var processing = (int)OutboxStatus.Processing;
        var lease = now.AddSeconds(_opts.ClaimLeaseSeconds);

        // Status + NextAttemptAt must be in the CTE projection to be updatable through it.
        const string sql = @"
WITH due AS (
    SELECT TOP({0}) Id, TenantId, ApplicationId, Status, NextAttemptAt
    FROM OutboxMessages AS m WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE m.Status IN ({1}, {2}) AND m.NextAttemptAt <= {3}
      AND NOT EXISTS (
          SELECT 1 FROM OutboxMessages AS older WITH (READCOMMITTEDLOCK)
          WHERE older.TenantId = m.TenantId AND older.ApplicationId = m.ApplicationId
            AND older.Id < m.Id AND older.Status IN ({1}, {2}))
    ORDER BY m.ApplicationId, m.Id
)
UPDATE due SET Status = {2}, NextAttemptAt = {4}
OUTPUT inserted.Id, inserted.TenantId, inserted.ApplicationId, inserted.NextAttemptAt AS Lease;";

        return await _db.Database
            .SqlQueryRaw<OutboxClaim>(sql, max, pending, processing, now, lease)
            .ToListAsync(ct);
    }
}
