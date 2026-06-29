using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IIntegrationSettingsService
{
    Task<TenantSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default);
    // Generates a new feed key, stores only its hash, and returns the plaintext (shown once).
    Task<string> GenerateFeedKeyAsync(CancellationToken ct = default);
}
