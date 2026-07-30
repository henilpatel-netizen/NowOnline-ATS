using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

// A stage bucket for the mini pipeline bar: stage name + active-application count, in stage order.
public sealed record JobStageCount(string Stage, int Count);

public sealed record JobListItem(
    int Id,
    string Title,
    string ExternalRef,
    JobStatus Status,
    string? Department,
    string? Location,
    DateTimeOffset? PublishedAt,
    int TotalApplications,
    int ActiveApplications,
    IReadOnlyList<JobStageCount> StageCounts,
    IReadOnlyList<string> TopApplicantNames);

public interface IJobListQuery
{
    Task<PagedResult<JobListItem>> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
}
