using Ats.Domain.Entities;

namespace Ats.Application.Locations;

public interface ILocationRepository
{
    Task<List<Location>> ListAsync(CancellationToken ct = default);
    Task<Location?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Location location, CancellationToken ct = default);
    Task RemoveAsync(Location location, CancellationToken ct = default);
    Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
