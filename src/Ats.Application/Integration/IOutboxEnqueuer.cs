namespace Ats.Application.Integration;

public interface IOutboxEnqueuer
{
    // Stages an OutboxMessage in the current unit of work (no SaveChanges) when this is the first
    // time the application reaches the stage, the application carries a SourceCode, and the tenant's
    // integration is enabled. The caller saves.
    Task StageAsync(int applicationId, int toStageId, CancellationToken ct = default);
}
