using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Candidates;

public sealed record CandidateListItem(
    int Id,
    string FullName,
    string Email,
    string? Phone,
    ApplicationOrigin LatestOrigin,
    string? LatestJobTitle,
    string? LatestStageName,
    int ApplicationCount,
    DateTimeOffset? LastActivity);

public interface ICandidateListQuery
{
    Task<PagedResult<CandidateListItem>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default);
}
