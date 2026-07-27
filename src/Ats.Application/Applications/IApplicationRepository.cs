using Ats.Domain.Entities;

namespace Ats.Application.Applications;

public interface IApplicationRepository
{
    Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default);
    Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default);
    Task<List<JobApplication>> ListForJobAsync(int jobId, CancellationToken ct = default);
    Task<JobApplication?> GetAsync(int id, CancellationToken ct = default);
    Task<JobApplication?> FindByCandidateJobAsync(int candidateId, int jobId, CancellationToken ct = default);
    Task AddApplicationAsync(JobApplication application, CancellationToken ct = default);
    Task AddEventAsync(ApplicationEvent ev, CancellationToken ct = default);
    Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default);
    void SetExpectedRowVersion(JobApplication application, byte[] rowVersion);
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default); // false on concurrency conflict
    Task SaveChangesAsync(CancellationToken ct = default);
}
