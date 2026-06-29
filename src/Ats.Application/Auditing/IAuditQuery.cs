using Ats.Domain.Entities;

namespace Ats.Application.Auditing;

public interface IAuditQuery
{
    Task<List<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
}
