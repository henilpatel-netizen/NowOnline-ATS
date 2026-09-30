using System.Text.Json;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxEnqueuer : IOutboxEnqueuer
{
    private readonly AtsDbContext _db;
    private readonly IntegrationOptions _opts;
    private readonly ILogger<OutboxEnqueuer> _logger;

    public OutboxEnqueuer(AtsDbContext db, IOptions<IntegrationOptions> opts, ILogger<OutboxEnqueuer> logger)
    {
        _db = db;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task StageAsync(int applicationId, int toStageId, CancellationToken ct = default)
    {
        // First arrival only: count SAVED events to this stage (the just-added event is not yet saved).
        var alreadyReached = await _db.ApplicationEvents
            .CountAsync(e => e.ApplicationId == applicationId && e.ToStageId == toStageId, ct);
        if (alreadyReached > 0) return;

        var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null || string.IsNullOrWhiteSpace(app.SourceCode)) return;

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (settings is null || !settings.IntegrationEnabled || settings.ReferralToolCustomerId is null) return;

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == app.JobId, ct);
        var candidate = await _db.Candidates.FirstOrDefaultAsync(c => c.Id == app.CandidateId, ct);
        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == toStageId, ct);
        if (job is null || candidate is null || stage is null) return;

        var status = string.IsNullOrWhiteSpace(stage.ReferralStatusOverride) ? stage.Name : stage.ReferralStatusOverride!;
        var code = app.SourceCode!.Trim();
        var externalCandidateId = candidate.Key.ToString("D");
        var identical = await _db.OutboxMessages
            .Where(ReferralToolRules.SameCandidateStatus(code, job.ExternalRef, externalCandidateId, status))
            .Select(m => new { m.Id, m.Status })
            .ToListAsync(ct);
        foreach (var earlier in identical)
        {
            var mayHaveBeenProcessed = earlier.Status == OutboxStatus.Failed
                && await ReferralToolRules.PossiblyProcessedStatusUpdates(_db.WebhookDeliveries, earlier.Id).AnyAsync(ct);
            if (!ReferralToolRules.BlocksIdenticalResend(earlier.Status, mayHaveBeenProcessed)) continue;

            // Code and the candidate key are referral / personal data; they stay out of the log.
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "Not queuing ReferralTool status {CandidateStatus} for application {ApplicationId}: identical message {MessageId} is {MessageStatus}.",
                    status, app.Id, earlier.Id, earlier.Status);
            return;
        }

        await _db.OutboxMessages.AddAsync(new OutboxMessage
        {
            ApplicationId = app.Id,
            Code = code,
            ExternalVacancyId = job.ExternalRef,
            ExternalCandidateId = externalCandidateId,
            CandidateStatus = status,
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTimeOffset.UtcNow
        }, ct);
        // No SaveChanges: the caller commits this with the stage event in one transaction.
    }

    public async Task StageVacancySyncAsync(Job job, CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        // Staged even while paused: the worker postpones these without counting attempts (SettingsProblem)
        // and a settings save pulls them forward, so a change made while paused is not lost.
        if (settings is null || settings.ReferralToolCustomerId is null) return;

        // Tenant is not an ITenantEntity; reading the job's own tenant row by id is not a filter bypass.
        var slug = await _db.Tenants.Where(t => t.Id == job.TenantId).Select(t => t.Slug).FirstAsync(ct);
        var location = job.LocationId is int locationId
            ? await _db.Locations.Where(l => l.Id == locationId).Select(l => l.City ?? l.Name).FirstOrDefaultAsync(ct)
            : null;
        var department = job.DepartmentId is int departmentId
            ? await _db.Departments.Where(d => d.Id == departmentId).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;

        await _db.OutboxMessages.AddAsync(new OutboxMessage
        {
            Kind = OutboxKind.VacancySync,
            JobId = job.Id,
            ExternalVacancyId = job.ExternalRef,
            Payload = JsonSerializer.Serialize(VacancyPayload.From(job, _opts.CareerSiteBaseUrl, slug, location, department)),
            Status = OutboxStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow
        }, ct);
        // No SaveChanges: the caller commits this with the job change.
    }
}
