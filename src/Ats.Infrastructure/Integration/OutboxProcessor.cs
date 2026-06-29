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
        if (msg is null || msg.Status != OutboxStatus.Pending) return OutboxOutcome.Skip;

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
        await LogAsync(msg.Id, DeliveryKind.CheckVacancy, check, exists, ct);
        if (!check.Reached || check.HttpStatus is < 200 or >= 300)
            return await DeferAsync(msg, $"Vacancy check failed ({check.HttpStatus}).", ct);
        if (!exists)
            return await DeferAsync(msg, "Vacancy not imported yet.", ct);

        var send = await _client.SendStatusUpdateAsync(settings,
            new StatusUpdateRequest(settings.CustomerId, msg.Code, msg.ExternalVacancyId, msg.ExternalCandidateId, msg.CandidateStatus), ct);
        await LogAsync(msg.Id, DeliveryKind.StatusUpdate, send, send.HttpStatus is >= 200 and < 300, ct);

        if (!send.Reached || send.HttpStatus >= 500)
            return await DeferAsync(msg, $"Transient send failure ({send.HttpStatus}).", ct);

        if (send.HttpStatus is >= 200 and < 300)
        {
            msg.Status = OutboxStatus.Delivered;
            msg.LastError = null;
            await _db.SaveChangesAsync(ct);
            return OutboxOutcome.Delivered;
        }

        // 4xx: terminal (duplicate event, unmapped status, bad code, validation).
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
        msg.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        await _db.SaveChangesAsync(ct);
        return OutboxOutcome.Transient;
    }

    private async Task LogAsync(int outboxMessageId, DeliveryKind kind, ReferralCallResult result, bool success, CancellationToken ct)
    {
        await _db.WebhookDeliveries.AddAsync(new WebhookDelivery
        {
            OutboxMessageId = outboxMessageId,
            Kind = kind,
            AttemptedAt = DateTimeOffset.UtcNow,
            HttpStatus = result.Reached ? result.HttpStatus : null,
            ResponseBody = Trunc(result.Body, 2000),
            Success = success
        }, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static string? Trunc(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
