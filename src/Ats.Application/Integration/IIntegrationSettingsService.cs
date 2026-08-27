using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IIntegrationSettingsService
{
    Task<TenantSettings> GetAsync(CancellationToken ct = default);
    // Returns false if another admin saved the settings since this edit was loaded (concurrency conflict).
    Task<bool> UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default);
    // Generates a new feed key, stores only its hash, and returns the plaintext (shown once).
    Task<string> GenerateFeedKeyAsync(CancellationToken ct = default);
}
