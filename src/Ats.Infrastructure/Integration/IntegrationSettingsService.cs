using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ats.Infrastructure.Integration;

public sealed class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly AtsDbContext _db;
    private readonly IVacancyFeedRepository _feed;
    private readonly IReferralToolClient _client;
    private readonly ILogger<IntegrationSettingsService> _logger;

    public IntegrationSettingsService(AtsDbContext db, IVacancyFeedRepository feed, IReferralToolClient client,
        ILogger<IntegrationSettingsService> logger)
    {
        _db = db;
        _feed = feed;
        _client = client;
        _logger = logger;
    }

    public async Task<TenantSettings> GetAsync(CancellationToken ct = default)
    {
        // Every tenant has exactly one settings row (created at onboarding).
        return await _db.TenantSettings.FirstAsync(ct);
    }

    public async Task<OperationResult> UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default)
    {
        if (ReferralToolBaseUrl.Validate(input.ReferralToolBaseUrl) is { } urlError)
            return OperationResult.Fail(urlError);

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
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Fail("These settings were changed by someone else. Reload the page and try again.");
        }

        if (settings.IntegrationEnabled)
            await WakePostponedMessagesAsync(settings.TenantId);
        return OperationResult.Ok;
    }

    // Postponed messages (settings problem, rejected credentials) otherwise wait up to MaxBackoffSeconds
    // after the owner fixes the settings. Best effort, after the save has succeeded: a row a worker claims
    // meanwhile is left alone, and the rows are detached so the audit write on this context is unaffected.
    // Any failure here is logged and swallowed: the settings are already committed.
    private async Task WakePostponedMessagesAsync(int tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        List<OutboxMessage> waiting = [];
        var count = 0;
        try
        {
            waiting = await _db.OutboxMessages
                .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt > now)
                .ToListAsync(CancellationToken.None);
            count = waiting.Count;
            if (count == 0) return;

            foreach (var m in waiting) m.NextAttemptAt = now;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Expected race: a worker claimed one of the rows, so the batch rolls back to its backoff.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not wake {Count} postponed outbox messages for tenant {TenantId}; they retry after their backoff.",
                count, tenantId);
        }
        finally
        {
            foreach (var m in waiting) _db.Entry(m).State = EntityState.Detached;
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
        if (ReferralToolRules.ConnectionProblem(s) is { } problem)
            return new ConnectionTestResult(false, problem);

        var (page, _) = await _feed.GetPageAsync(1, 1, ct);
        var sampleRef = page.FirstOrDefault()?.ExternalRef;
        if (sampleRef is null)
            return new ConnectionTestResult(false, "Publish a job first so there is a vacancy to test with.");

        var settings = new ReferralToolSettings(
            s.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId!.Value);
        var (result, exists) = await _client.CheckVacancyExistsAsync(settings, sampleRef, ct);
        var ok = result.Reached && result.HttpStatus is >= 200 and < 300 && exists is not null;
        // Not reached: Body is the transport exception message (no request headers), so it names the cause.
        var reason = result.Reached || string.IsNullOrWhiteSpace(result.Body)
            ? ""
            : $" Reason: {(result.Body.Length <= 300 ? result.Body : result.Body[..300])}";
        return new ConnectionTestResult(ok,
            $"Test for {sampleRef}: reached={result.Reached}, HTTP {result.HttpStatus}, vacancy exists={exists?.ToString() ?? "unknown"}.{reason}");
    }
}
