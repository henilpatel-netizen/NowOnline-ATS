using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IOutboxEnqueuer
{
    // Stages an OutboxMessage in the current unit of work (no SaveChanges) when this is the first
    // time the application reaches the stage, the application carries a SourceCode, and the tenant's
    // integration is enabled. The caller saves.
    Task StageAsync(int applicationId, int toStageId, CancellationToken ct = default);

    // Stages a VacancySync snapshot of the job in the current unit of work (no SaveChanges) whenever the
    // tenant has a ReferralTool customer id, even while the integration is paused (the worker holds it
    // until re-enabled). The caller saves, so the job change and its sync commit together.
    Task StageVacancySyncAsync(Job job, CancellationToken ct = default);
}
