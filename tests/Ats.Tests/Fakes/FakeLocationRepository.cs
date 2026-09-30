using Ats.Application.Locations;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

public sealed class FakeLocationRepository : ILocationRepository
{
    public List<Location> Locations { get; } = new();
    public HashSet<int> UsedByJob { get; } = new();
    public int SaveCount { get; private set; }

    public Task<List<Location>> ListAsync(CancellationToken ct = default) => Task.FromResult(Locations.ToList());

    public Task<Location?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Locations.FirstOrDefault(l => l.Id == id));

    public Task AddAsync(Location location, CancellationToken ct = default)
    {
        Locations.Add(location);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Location location, CancellationToken ct = default)
    {
        Locations.Remove(location);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) => Task.FromResult(UsedByJob.Contains(id));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
