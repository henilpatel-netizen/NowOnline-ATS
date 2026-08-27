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

    public async Task<bool> UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default)
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

        // Concurrency: pin the version the editor loaded so a competing save is detected. Force an
        // UPDATE (mark a column modified) so the token is always checked, even if no field changed.
        if (input.RowVersion is { Length: > 0 })
        {
            var entry = _db.Entry(settings);
            entry.Property(s => s.RowVersion).OriginalValue = input.RowVersion;
            entry.Property(s => s.CodeParameterName).IsModified = true;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
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
