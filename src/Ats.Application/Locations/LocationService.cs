using Ats.Application.Departments; // for OperationResult
using Ats.Domain.Entities;

namespace Ats.Application.Locations;

public interface ILocationService
{
    Task<List<Location>> ListAsync(CancellationToken ct = default);
    Task<Location?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(string name, string? city, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(int id, string name, string? city, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class LocationService : ILocationService
{
    private readonly ILocationRepository _repo;
    public LocationService(ILocationRepository repo) => _repo = repo;

    public Task<List<Location>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Location?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(string name, string? city, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        await _repo.AddAsync(new Location { Name = name.Trim(), City = city?.Trim() }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(int id, string name, string? city, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        var loc = await _repo.GetAsync(id, ct);
        if (loc is null) return OperationResult.Fail("Location not found.");
        loc.Name = name.Trim();
        loc.City = city?.Trim();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var loc = await _repo.GetAsync(id, ct);
        if (loc is null) return OperationResult.Fail("Location not found.");
        if (await _repo.IsReferencedByJobAsync(id, ct))
            return OperationResult.Fail("This location is used by one or more jobs and cannot be deleted.");
        await _repo.RemoveAsync(loc, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
