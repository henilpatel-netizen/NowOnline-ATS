using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IIntegrationSettingsService
{
    Task<TenantSettings> GetAsync(CancellationToken ct = default);
    // Returns false if another admin saved the settings since this edit was loaded (concurrency conflict).
    Task<bool> UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default);
    // Generates a new feed key, stores only its hash, and returns the plaintext (shown once).
    Task<string> GenerateFeedKeyAsync(CancellationToken ct = default);

    // Outbox counts for the health banner, from a single grouped query (QUAL-3).
    Task<OutboxCounts> GetOutboxCountsAsync(CancellationToken ct = default);

    // Orchestrates the "Test connection" probe: validates the settings are complete, picks a sample
    // published vacancy, and calls ReferralTool. Was inline HTTP interpretation in the controller.
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

// Pending includes Processing: a message claimed by a worker is still pending to a user.
public sealed record OutboxCounts(int Delivered, int Failed, int Pending);

public sealed record ConnectionTestResult(bool Succeeded, string Message);
