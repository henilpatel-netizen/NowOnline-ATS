using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Ats.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxProcessor : IOutboxProcessor
{
    private readonly AtsDbContext _db;
    private readonly WorkerTenantContext _tenant;
    private readonly IReferralToolClient _client;
    private readonly IntegrationOptions _opts;

    public OutboxProcessor(AtsDbContext db, WorkerTenantContext tenant, IReferralToolClient client, IOptions<IntegrationOptions> opts)
    {
        _db = db; _tenant = tenant; _client = client; _opts = opts.Value;
    }

    public async Task<OutboxOutcome> ProcessAsync(OutboxClaim claim, CancellationToken ct = default)
    {
        var msg = await LoadClaimedAsync(claim, ct);
        if (msg is null)
            return OutboxOutcome.Skip;

        var s = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (ReferralToolRules.SettingsProblem(s) is { } problem)
            return await PostponeAsync(msg, problem, ct);

        var settings = new ReferralToolSettings(
            s!.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId!.Value);

        // Pre-flight: only send once ReferralTool has imported the vacancy.
        var (check, exists) = await _client.CheckVacancyExistsAsync(settings, msg.ExternalVacancyId, ct);
        Record(Log(msg.Id, DeliveryKind.CheckVacancy), check, exists == true);
        if (check.Reached && ReferralToolRules.IsCredentialRejection(check.HttpStatus))
            return await PostponeAsync(msg, CredentialsRejected(check.HttpStatus), ct);
        if (!check.Reached || check.HttpStatus is < 200 or >= 300)
            return await DeferAsync(msg, $"Vacancy check failed ({check.HttpStatus}).", ct);
        if (exists is null)
            return await DeferAsync(msg, "Vacancy check returned an unreadable response.", ct);
        if (exists == false)
            return await DeferAsync(msg, "Vacancy not imported yet.", ct);

        // Read before this attempt's row exists, so it only sees earlier sends of this message.
        var hadPossiblyProcessedAttempt = await _db.WebhookDeliveries
            .Where(d => d.OutboxMessageId == msg.Id && d.Kind == DeliveryKind.StatusUpdate)
            .AnyAsync(ReferralToolRules.MayHaveBeenProcessed, ct);

        // Record the attempt before sending and fill in the reply after, both before the message is
        // touched (it is unmodified here, so these saves write only attempt rows). If the worker dies
        // or a later save fails after ReferralTool accepted the update, the row is on file and the
        // re-send is recognised as a duplicate, not a failure. Not cancellable once the send is
        // committed to: a stopping worker must not leave a call unrecorded.
        var attempt = Log(msg.Id, DeliveryKind.StatusUpdate);
        attempt.ResponseBody = "Sending; no reply recorded.";
        await _db.SaveChangesAsync(CancellationToken.None);

        var send = await _client.SendStatusUpdateAsync(settings,
            new StatusUpdateRequest(settings.CustomerId, msg.Code, msg.ExternalVacancyId, msg.ExternalCandidateId, msg.CandidateStatus), ct);

        var outcome = ReferralToolRules.ClassifyStatusUpdate(send.Reached, send.HttpStatus, hadPossiblyProcessedAttempt);
        Record(attempt, send, outcome == OutboxOutcome.Delivered);
        await _db.SaveChangesAsync(CancellationToken.None);

        if (outcome == OutboxOutcome.Postpone)
            return await PostponeAsync(msg, CredentialsRejected(send.HttpStatus), CancellationToken.None);

        if (outcome == OutboxOutcome.Transient)
            return await DeferAsync(msg, $"Transient send failure ({send.HttpStatus}).", ct);

        if (outcome == OutboxOutcome.Delivered)
        {
            msg.Status = OutboxStatus.Delivered;
            msg.LastError = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            return OutboxOutcome.Delivered;
        }

        // 4xx with no earlier attempt ReferralTool may have recorded: terminal (unmapped status, bad
        // code, validation).
        msg.Status = OutboxStatus.Failed;
        msg.LastError = Trunc($"{send.HttpStatus}: {send.Body}", 1000);
        await _db.SaveChangesAsync(CancellationToken.None);
        return OutboxOutcome.Failed;
    }

    public async Task RecordFailureAsync(OutboxClaim claim, string error, CancellationToken ct = default)
    {
        var msg = await LoadClaimedAsync(claim, ct);
        if (msg is not null)
            await DeferAsync(msg, error, ct);
    }

    private async Task<OutboxMessage?> LoadClaimedAsync(OutboxClaim claim, CancellationToken ct)
    {
        _tenant.CurrentTenantId = claim.TenantId; // scope everything below to this tenant

        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == claim.Id, ct);
        // Only a row still carrying our lease is ours; anything else was released or reclaimed.
        return msg is not null && claim.IsOwnedBy(msg.Status, msg.NextAttemptAt) ? msg : null;
    }

    // Settings the owner can fix (paused, incomplete, invalid URL, rejected credentials): wait without
    // spending an attempt, so queued messages survive and go out once the settings are usable again.
    private async Task<OutboxOutcome> PostponeAsync(OutboxMessage msg, string reason, CancellationToken ct)
    {
        msg.LastError = Trunc(reason, 1000);
        msg.Status = OutboxStatus.Pending;
        msg.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(_opts.MaxBackoffSeconds);
        await _db.SaveChangesAsync(ct);
        return OutboxOutcome.Postpone;
    }

    private static string CredentialsRejected(int httpStatus) =>
        $"ReferralTool rejected the credentials (HTTP {httpStatus}); check the X-Api-Key and X-Auth-Token.";

    private async Task<OutboxOutcome> DeferAsync(OutboxMessage msg, string error, CancellationToken ct)
    {
        msg.Attempts++;
        msg.LastError = Trunc(error, 1000);
        if (msg.Attempts >= _opts.MaxAttempts)
        {
            msg.Status = OutboxStatus.Failed;
            await _db.SaveChangesAsync(ct);
            return OutboxOutcome.Failed;
        }
        var seconds = Math.Min(_opts.MaxBackoffSeconds, _opts.BaseBackoffSeconds * Math.Pow(2, msg.Attempts));
        // Release the claim: back to Pending so it is retried at NextAttemptAt (backoff overrides the lease).
        msg.Status = OutboxStatus.Pending;
        msg.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        await _db.SaveChangesAsync(ct);
        return OutboxOutcome.Transient;
    }

    // Stages the attempt row only; the caller's next SaveChanges writes it. A message that reaches the
    // status update costs three round-trips (attempt before send, reply after, then status); earlier
    // exits cost one.
    private WebhookDelivery Log(int outboxMessageId, DeliveryKind kind)
    {
        var row = new WebhookDelivery { OutboxMessageId = outboxMessageId, Kind = kind, AttemptedAt = DateTimeOffset.UtcNow };
        _db.WebhookDeliveries.Add(row);
        return row;
    }

    private static void Record(WebhookDelivery row, ReferralCallResult result, bool success)
    {
        row.HttpStatus = ReferralToolRules.RecordedStatus(result);
        row.ResponseBody = Trunc(result.Body, 2000);
        row.Success = success;
    }

    private static string? Trunc(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
