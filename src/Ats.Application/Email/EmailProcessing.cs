using Ats.Domain.Enums;

namespace Ats.Application.Email;

// Lease is the NextAttemptAt the claim wrote: a fencing token, as in OutboxClaim.
public sealed record EmailClaim(int Id, int? TenantId, DateTimeOffset Lease)
{
    public bool IsOwnedBy(OutboxStatus status, DateTimeOffset nextAttemptAt) =>
        status == OutboxStatus.Processing && nextAttemptAt == Lease;
}

public interface IEmailClaimStore
{
    // Due messages, independent of each other (no ordering); each claim carries its lease.
    Task<List<EmailClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default);
}

public interface IEmailProcessor
{
    // Sends one claimed message and settles it (EmailRules.Settle). Null when the claim is no longer ours.
    Task<EmailSendOutcome?> ProcessAsync(EmailClaim claim, CancellationToken ct = default);

    // Counts a ProcessAsync that threw as a transient failure. Call it on a fresh processor.
    Task RecordFailureAsync(EmailClaim claim, string error, CancellationToken ct = default);
}
