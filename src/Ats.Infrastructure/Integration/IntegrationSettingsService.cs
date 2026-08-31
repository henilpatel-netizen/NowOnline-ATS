using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly AtsDbContext _db;
    private readonly IVacancyFeedRepository _feed;
    private readonly IReferralToolClient _client;

    public IntegrationSettingsService(AtsDbContext db, IVacancyFeedRepository feed, IReferralToolClient client)
    {
        _db = db;
        _feed = feed;
        _client = client;
    }

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

    public async Task<OutboxCounts> GetOutboxCountsAsync(CancellationToken ct = default)
    {
        // One grouped query, where the banner previously issued four separate COUNTs (QUAL-3).
        var byStatus = await _db.OutboxMessages.AsNoTracking()
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        int Count(OutboxStatus s) => byStatus.TryGetValue(s, out var n) ? n : 0;

        // Processing = claimed by a worker and in flight; still pending from the user's point of view.
        return new OutboxCounts(
            Count(OutboxStatus.Delivered),
            Count(OutboxStatus.Failed),
            Count(OutboxStatus.Pending) + Count(OutboxStatus.Processing));
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var s = await _db.TenantSettings.FirstAsync(ct);
        if (s.ReferralToolCustomerId is null || string.IsNullOrWhiteSpace(s.ReferralToolBaseUrl)
            || string.IsNullOrWhiteSpace(s.ReferralToolApiKey) || string.IsNullOrWhiteSpace(s.ReferralToolAuthToken))
        {
            return new ConnectionTestResult(false, "Fill base URL, customer id, X-Api-Key, and X-Auth-Token first.");
        }

        var (page, _) = await _feed.GetPageAsync(1, 1, ct);
        var sampleRef = page.FirstOrDefault()?.ExternalRef;
        if (sampleRef is null)
            return new ConnectionTestResult(false, "Publish a job first so there is a vacancy to test with.");

        var settings = new ReferralToolSettings(
            s.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId.Value);
        var (result, exists) = await _client.CheckVacancyExistsAsync(settings, sampleRef, ct);
        var ok = result.Reached && result.HttpStatus is >= 200 and < 300;
        return new ConnectionTestResult(ok,
            $"Test for {sampleRef}: reached={result.Reached}, HTTP {result.HttpStatus}, vacancy exists={exists}.");
    }
}
