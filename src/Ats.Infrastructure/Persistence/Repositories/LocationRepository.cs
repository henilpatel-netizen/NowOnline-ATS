using Ats.Application.Locations;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class LocationRepository : ILocationRepository
{
    private readonly AtsDbContext _db;
    public LocationRepository(AtsDbContext db) => _db = db;

    public Task<List<Location>> ListAsync(CancellationToken ct = default) =>
        _db.Locations.OrderBy(l => l.Name).ToListAsync(ct);

    public Task<Location?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task AddAsync(Location location, CancellationToken ct = default) =>
        await _db.Locations.AddAsync(location, ct);

    public Task RemoveAsync(Location location, CancellationToken ct = default)
    {
        _db.Locations.Remove(location);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.AnyAsync(j => j.LocationId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
