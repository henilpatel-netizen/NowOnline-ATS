namespace Ats.Application.Integration;

public interface IDeliveryLogService
{
    // Most recent outbox messages for the current tenant, each with its delivery attempts.
    Task<List<DeliveryLogEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
}
