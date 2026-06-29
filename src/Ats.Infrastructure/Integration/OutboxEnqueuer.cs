using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxEnqueuer : IOutboxEnqueuer
{
    private readonly AtsDbContext _db;
    public OutboxEnqueuer(AtsDbContext db) => _db = db;

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

        await _db.OutboxMessages.AddAsync(new OutboxMessage
        {
            ApplicationId = app.Id,
            Code = app.SourceCode!.Trim(),
            ExternalVacancyId = job.ExternalRef,
            ExternalCandidateId = candidate.Key.ToString("D"),
            CandidateStatus = status,
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTimeOffset.UtcNow
        }, ct);
        // No SaveChanges: the caller commits this with the stage event in one transaction.
    }
}
