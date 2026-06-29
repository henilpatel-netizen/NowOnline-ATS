using Ats.Domain.Entities;

namespace Ats.Application.Jobs;

public interface IJobRepository
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task<int> NextJobNumberAsync(CancellationToken ct = default);
    Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
