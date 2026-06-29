namespace Ats.Application.Integration;

public sealed record OutboxClaim(int Id, int TenantId, int ApplicationId);

public enum OutboxOutcome { Delivered, Transient, Failed, Skip }

public interface IOutboxClaimStore
{
    // Due Pending messages across ALL tenants, oldest-first per application (filter-bypass read).
    Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default);
}

public interface IOutboxProcessor
{
    // Processes one message: sets the worker tenant, pre-checks the vacancy, posts the status update,
    // updates the message, and logs a WebhookDelivery.
    Task<OutboxOutcome> ProcessAsync(OutboxClaim claim, CancellationToken ct = default);
}
