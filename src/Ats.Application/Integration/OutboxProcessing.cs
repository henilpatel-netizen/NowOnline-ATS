using Ats.Domain.Enums;

namespace Ats.Application.Integration;

// Lease is the NextAttemptAt the claim wrote. It is a fencing token: once another worker reclaims the
// row it writes a new lease, so a stale worker can tell the row is no longer its own.
public sealed record OutboxClaim(int Id, int TenantId, int ApplicationId, DateTimeOffset Lease)
{
    public bool IsOwnedBy(OutboxStatus status, DateTimeOffset nextAttemptAt) =>
        status == OutboxStatus.Processing && nextAttemptAt == Lease;
}

// Postpone: retried later without counting an attempt (a settings problem the owner can fix).
public enum OutboxOutcome { Delivered, Transient, Failed, Skip, Postpone }

public interface IOutboxClaimStore
{
    // Due messages across ALL tenants (filter-bypass read), never one with an older undelivered message
    // of the same application; each claim carries the lease it was taken with.
    Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default);
}

public interface IOutboxProcessor
{
    // Processes one message: sets the worker tenant, pre-checks the vacancy, posts the status update,
    // updates the message, and logs a WebhookDelivery.
    Task<OutboxOutcome> ProcessAsync(OutboxClaim claim, CancellationToken ct = default);

    // Counts a ProcessAsync that threw as a failed attempt (backoff, dead-letter at MaxAttempts). Call
    // it on a fresh processor: the one that threw may hold a broken unit of work.
    Task RecordFailureAsync(OutboxClaim claim, string error, CancellationToken ct = default);
}

public static class OutboxBatch
{
    // Ordering is the claim's job: ClaimDueAsync hands out at most one message per application, so the
    // claims here are independent. A failure (thrown) is recorded and the others carry on; cancellation
    // on shutdown propagates, it is not the message's fault. A claim whose lease has expired is left
    // alone (not processed, no failure recorded): another worker may already own it, and it comes back
    // through the claim anyway.
    public static async Task RunAsync(IEnumerable<OutboxClaim> claims,
        Func<OutboxClaim, Task<OutboxOutcome>> process,
        Func<OutboxClaim, Exception, Task> onFailure,
        Func<DateTimeOffset> utcNow,
        CancellationToken stoppingToken)
    {
        foreach (var claim in claims)
        {
            if (claim.Lease <= utcNow()) continue;

            try
            {
                await process(claim);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                await onFailure(claim, ex);
            }
        }
    }
}
