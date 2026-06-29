using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly AtsDbContext _db;
    public IntegrationSettingsService(AtsDbContext db) => _db = db;

    public async Task<TenantSettings> GetAsync(CancellationToken ct = default)
    {
        // Every tenant has exactly one settings row (created at onboarding).
        return await _db.TenantSettings.FirstAsync(ct);
    }

    public async Task UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct);
        settings.IntegrationEnabled = input.IntegrationEnabled;
        settings.ReferralToolBaseUrl = string.IsNullOrWhiteSpace(input.ReferralToolBaseUrl) ? null : input.ReferralToolBaseUrl.Trim();
        settings.ReferralToolCustomerId = input.ReferralToolCustomerId;
        settings.CodeParameterName = string.IsNullOrWhiteSpace(input.CodeParameterName) ? "ref" : input.CodeParameterName.Trim();

        // Secrets: only overwrite when a new non-blank value is supplied.
        if (!string.IsNullOrWhiteSpace(input.ReferralToolAuthToken))
            settings.ReferralToolAuthToken = input.ReferralToolAuthToken.Trim();
        if (!string.IsNullOrWhiteSpace(input.ReferralToolApiKey))
            settings.ReferralToolApiKey = input.ReferralToolApiKey.Trim();

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> GenerateFeedKeyAsync(CancellationToken ct = default)
    {
        var key = FeedApiKey.Generate();
        var settings = await _db.TenantSettings.FirstAsync(ct);
        settings.FeedApiKeyHash = FeedApiKey.Hash(key);
        await _db.SaveChangesAsync(ct);
        return key;
    }
}
