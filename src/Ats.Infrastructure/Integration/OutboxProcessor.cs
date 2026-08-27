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
        _tenant.CurrentTenantId = claim.TenantId; // scope everything below to this tenant

        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == claim.Id, ct);
        // Processing = claimed by this worker (atomic claim). Pending is tolerated for safety.
        if (msg is null || (msg.Status != OutboxStatus.Processing && msg.Status != OutboxStatus.Pending))
            return OutboxOutcome.Skip;

        var s = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (s is null || !s.IntegrationEnabled || s.ReferralToolCustomerId is null
            || string.IsNullOrWhiteSpace(s.ReferralToolBaseUrl)
            || string.IsNullOrWhiteSpace(s.ReferralToolApiKey)
            || string.IsNullOrWhiteSpace(s.ReferralToolAuthToken))
        {
            return await DeferAsync(msg, "Integration settings incomplete or disabled.", ct);
        }

        var settings = new ReferralToolSettings(
            s.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId.Value);

        // Pre-flight: only send once ReferralTool has imported the vacancy.
        var (check, exists) = await _client.CheckVacancyExistsAsync(settings, msg.ExternalVacancyId, ct);
        Log(msg.Id, DeliveryKind.CheckVacancy, check, exists);
        if (!check.Reached || check.HttpStatus is < 200 or >= 300)
            return await DeferAsync(msg, $"Vacancy check failed ({check.HttpStatus}).", ct);
        if (!exists)
            return await DeferAsync(msg, "Vacancy not imported yet.", ct);

        // Did we already POST this exact event on an earlier attempt? If so, this message must have
        // been transient before (a 2xx would have marked it Delivered; a first-time 4xx, Failed —
        // neither re-processes). The payload is a frozen snapshot, so any validation-type 4xx is
        // deterministic and would have shown on the first attempt. Therefore a 4xx that appears only
        // now is ReferralTool's duplicate guard rejecting our re-send of an event it already recorded.
        var hadPriorStatusAttempt = await _db.WebhookDeliveries
            .AnyAsync(d => d.OutboxMessageId == msg.Id && d.Kind == DeliveryKind.StatusUpdate, ct);

        var send = await _client.SendStatusUpdateAsync(settings,
            new StatusUpdateRequest(settings.CustomerId, msg.Code, msg.ExternalVacancyId, msg.ExternalCandidateId, msg.CandidateStatus), ct);

        var is2xx = send.HttpStatus is >= 200 and < 300;
        var is4xx = send.Reached && send.HttpStatus is >= 400 and < 500;
        // Idempotent re-delivery: the contract forbids an idempotency-key field (frozen payload), so we
        // dedupe on our side. A duplicate-guard 4xx after a prior attempt means ReferralTool has it.
        var idempotentDuplicate = is4xx && hadPriorStatusAttempt;
        Log(msg.Id, DeliveryKind.StatusUpdate, send, is2xx || idempotentDuplicate);

        if (!send.Reached || send.HttpStatus >= 500)
            return await DeferAsync(msg, $"Transient send failure ({send.HttpStatus}).", ct);

        if (is2xx || idempotentDuplicate)
        {
            msg.Status = OutboxStatus.Delivered;
            msg.LastError = null;
            await _db.SaveChangesAsync(ct);
            return OutboxOutcome.Delivered;
        }

        // First-time 4xx: terminal (unmapped status, bad code, validation).
        msg.Status = OutboxStatus.Failed;
        msg.LastError = Trunc($"{send.HttpStatus}: {send.Body}", 1000);
        await _db.SaveChangesAsync(ct);
        return OutboxOutcome.Failed;
    }

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

    // Stages the attempt row only. Every exit path below ends in exactly one SaveChanges, so a
    // message costs one database round-trip instead of one per attempt log plus one per status change.
    private void Log(int outboxMessageId, DeliveryKind kind, ReferralCallResult result, bool success)
    {
        _db.WebhookDeliveries.Add(new WebhookDelivery
        {
            OutboxMessageId = outboxMessageId,
            Kind = kind,
            AttemptedAt = DateTimeOffset.UtcNow,
            HttpStatus = result.Reached ? result.HttpStatus : null,
            ResponseBody = Trunc(result.Body, 2000),
            Success = success
        });
    }

    private static string? Trunc(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
