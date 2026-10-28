using Ats.Application.Email;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ats.Infrastructure.Email;

public sealed class EmailClaimStore : IEmailClaimStore
{
    private readonly AtsDbContext _db;
    private readonly EmailOptions _opts;

    public EmailClaimStore(AtsDbContext db, IOptions<EmailOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    // Same atomic claim as OutboxClaimStore, minus the ordering NOT EXISTS: emails are independent.
    // EmailMessage is not a tenant entity, so this bypasses no query filter.
    public async Task<List<EmailClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default)
    {
        var pending = (int)OutboxStatus.Pending;
        var processing = (int)OutboxStatus.Processing;
        var lease = now.AddSeconds(_opts.ClaimLeaseSeconds);

        // Status + NextAttemptAt must be in the CTE projection to be updatable through it.
        const string sql = @"
WITH due AS (
    SELECT TOP({0}) Id, TenantId, Status, NextAttemptAt
    FROM EmailMessages AS m WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE m.Status IN ({1}, {2}) AND m.NextAttemptAt <= {3}
    ORDER BY m.Id
)
UPDATE due SET Status = {2}, NextAttemptAt = {4}
OUTPUT inserted.Id, inserted.TenantId, inserted.NextAttemptAt AS Lease;";

        return await _db.Database
            .SqlQueryRaw<EmailClaim>(sql, max, pending, processing, now, lease)
            .ToListAsync(ct);
    }
}
