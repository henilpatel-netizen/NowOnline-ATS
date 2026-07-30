using Ats.Application.Common;
using Ats.Domain.Entities;

namespace Ats.Application.Auditing;

public interface IAuditQuery
{
    Task<List<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
    Task<PagedResult<AuditEntry>> SearchAsync(
        string? q, string? action, DateTimeOffset? from, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default);
}
