using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;

namespace Ats.Application.Career;

public interface ICareerService
{
    Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default);
    Task<Job?> GetPublishedJobAsync(string externalRef, CancellationToken ct = default);
    Task<string> GetCodeParameterNameAsync(CancellationToken ct = default);
    Task<OperationResult> ApplyAsync(ApplyInput input, CancellationToken ct = default);
}
