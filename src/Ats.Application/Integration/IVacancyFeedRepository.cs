using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IVacancyFeedRepository
{
    // Non-draft jobs for the current tenant (Published + Closed), paginated, with Location loaded.
    Task<(List<Job> Jobs, int Total)> GetPageAsync(int page, int perPage, CancellationToken ct = default);

    // Records "the feed was pulled just now" on the current tenant's settings, debounced to at most
    // once a minute. Safe to call on every feed request.
    Task TouchFeedPulledAsync(CancellationToken ct = default);
}
