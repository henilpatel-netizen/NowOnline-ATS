using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IVacancyFeedRepository
{
    // Non-draft jobs for the current tenant (Published + Closed), paginated, with Location loaded.
    Task<(List<Job> Jobs, int Total)> GetPageAsync(int page, int perPage, CancellationToken ct = default);
}
