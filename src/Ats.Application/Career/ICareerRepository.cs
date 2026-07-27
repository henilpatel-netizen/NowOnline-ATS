using Ats.Domain.Entities;

namespace Ats.Application.Career;

public interface ICareerRepository
{
    Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default);
    Task<Job?> GetPublishedJobByExternalRefAsync(string externalRef, CancellationToken ct = default);
    Task<string> GetCodeParameterNameAsync(CancellationToken ct = default);
}
