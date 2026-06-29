using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public interface IPipelineTemplateRepository
{
    Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default);
    Task<PipelineTemplate?> GetWithStagesAsync(int id, CancellationToken ct = default);
    Task AddAsync(PipelineTemplate template, CancellationToken ct = default);
    Task RemoveStagesAsync(IEnumerable<PipelineStage> stages, CancellationToken ct = default);
    Task RemoveAsync(PipelineTemplate template, CancellationToken ct = default);
    Task<bool> IsUsedByJobAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
