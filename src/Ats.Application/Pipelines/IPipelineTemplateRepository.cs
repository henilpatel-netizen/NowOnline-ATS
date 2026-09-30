using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public interface IPipelineTemplateRepository
{
    Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default);
    Task<PipelineTemplate?> GetWithStagesAsync(int id, CancellationToken ct = default);
    Task AddAsync(PipelineTemplate template, CancellationToken ct = default);
    Task RemoveStagesAsync(IEnumerable<PipelineStage> stages, CancellationToken ct = default);
    // Removes the template and its stages and saves. False when a row the query filter hides
    // (a soft-deleted job or application) still references it, so the database refuses the delete.
    Task<bool> TryRemoveAsync(PipelineTemplate template, CancellationToken ct = default);
    Task<bool> IsUsedByJobAsync(int id, CancellationToken ct = default);
    Task<Dictionary<int, int>> JobCountsByTemplateAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    // Optimistic concurrency: pin the template's expected RowVersion (from page load) and save.
    // Conflict when another edit won the race; StageInUse when a removed stage is still the current
    // stage of an application (including soft-deleted ones the query filter hides).
    void SetExpectedRowVersion(PipelineTemplate template, byte[] rowVersion);
    Task<TemplateSaveOutcome> TrySaveChangesAsync(CancellationToken ct = default);
}

public enum TemplateSaveOutcome { Saved, Conflict, StageInUse }
