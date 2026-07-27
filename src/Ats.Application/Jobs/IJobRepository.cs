using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

public interface IJobRepository
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    Task<(List<Job> Jobs, int Total)> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task<int> NextJobNumberAsync(CancellationToken ct = default);
    Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
}
