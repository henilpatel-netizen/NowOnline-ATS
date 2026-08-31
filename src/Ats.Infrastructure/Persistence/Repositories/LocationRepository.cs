using System.Linq.Expressions;
using Ats.Application.Locations;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class LocationRepository : NamedLookupRepository<Location>, ILocationRepository
{
    public LocationRepository(AtsDbContext db) : base(db) { }

    protected override DbSet<Location> Set => Db.Locations;
    protected override Expression<Func<Location, string>> NameSelector => l => l.Name;
    protected override Expression<Func<Job, bool>> ReferencedByJob(int id) => j => j.LocationId == id;
}
