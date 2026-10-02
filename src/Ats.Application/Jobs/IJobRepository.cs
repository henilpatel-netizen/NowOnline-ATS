using Ats.Domain.Entities;

namespace Ats.Application.Jobs;

public interface IJobRepository
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task<Job?> GetWithHiringManagersAsync(int id, CancellationToken ct = default);
    // One query on the filtered context: the ids among userIds that are active HiringManagers in this tenant.
    Task<IReadOnlyList<int>> FindAssignableHiringManagerIdsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);
    // Display names of this tenant's users, active or not.
    Task<IReadOnlyDictionary<int, string>> GetUserNamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);
    Task<bool> IsAssignedToAsync(int jobId, int userId, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task<int> NextJobNumberAsync(CancellationToken ct = default);
    Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    // False only when a concurrent create took the same per-tenant job number (ExternalRef); anything else throws.
    Task<bool> TrySaveNewJobAsync(CancellationToken ct = default);
    // False only when the save hits the unique (tenant, job, user) hiring team index; anything else throws.
    Task<bool> TrySaveTeamChangesAsync(CancellationToken ct = default);
}
