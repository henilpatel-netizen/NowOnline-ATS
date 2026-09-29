using Ats.Application.Common;
using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IIntegrationSettingsService
{
    Task<TenantSettings> GetAsync(CancellationToken ct = default);
    // Fails on an invalid base URL, or if another admin saved the settings since this edit was loaded.
    Task<OperationResult> UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default);
    // Published jobs, shown on the vacancy card.
    Task<int> CountPublishedJobsAsync(CancellationToken ct = default);

    // Stages a vacancy sync for every non-draft job (first switch-on, or after the integration was off).
    // Returns null when the settings cannot deliver, otherwise how many were queued (0 = no published or closed jobs).
    Task<int?> QueueVacancySyncAsync(CancellationToken ct = default);

    // Outbox counts for the health banner, from a single grouped query (QUAL-3).
    Task<OutboxCounts> GetOutboxCountsAsync(CancellationToken ct = default);

    // Orchestrates the "Test connection" probe: validates the settings are complete, picks a sample
    // published vacancy, and calls ReferralTool. Was inline HTTP interpretation in the controller.
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

// Pending includes Processing: a message claimed by a worker is still pending to a user.
public sealed record OutboxCounts(int Delivered, int Failed, int Pending);

public sealed record ConnectionTestResult(bool Succeeded, string Message);
