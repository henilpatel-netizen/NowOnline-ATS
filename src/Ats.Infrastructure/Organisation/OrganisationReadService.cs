using Ats.Application.Organisation;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Organisation;

public sealed class OrganisationReadService : IOrganisationReadService
{
    private readonly AtsDbContext _db;
    public OrganisationReadService(AtsDbContext db) => _db = db;

    public async Task<OrganisationOverview> GetAsync(CancellationToken ct = default)
    {
        // The global query filter already excludes soft-deleted jobs and scopes by tenant.
        var deptCounts = await _db.Jobs.Where(j => j.DepartmentId != null)
            .GroupBy(j => j.DepartmentId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var locCounts = await _db.Jobs.Where(j => j.LocationId != null)
            .GroupBy(j => j.LocationId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var departments = await _db.Departments.OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name }).ToListAsync(ct);
        var locations = await _db.Locations.OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.City }).ToListAsync(ct);

        return new OrganisationOverview(
            departments.Select(d => new OrgDepartment(d.Id, d.Name, deptCounts.TryGetValue(d.Id, out var dc) ? dc : 0)).ToList(),
            locations.Select(l => new OrgLocation(l.Id, l.Name, l.City, locCounts.TryGetValue(l.Id, out var lc) ? lc : 0)).ToList());
    }
}
