using System.Linq.Expressions;
using Ats.Domain.Common;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

// Department and Location repositories were structurally identical apart from the DbSet, the
// ordering property and which Job foreign key counts as "referenced" (QUAL-5). Adding a third lookup
// type should not mean a third copy of the same six methods.
//
// Deliberately a small base class, not a general-purpose generic repository: it exists only for
// tenant-scoped name lookups guarded against deletion while a job points at them.
public abstract class NamedLookupRepository<TEntity> where TEntity : TenantEntity
{
    protected AtsDbContext Db { get; }

    protected NamedLookupRepository(AtsDbContext db) => Db = db;

    protected abstract DbSet<TEntity> Set { get; }

    // The display name, used for the default ordering.
    protected abstract Expression<Func<TEntity, string>> NameSelector { get; }

    // Which Job foreign key points at this lookup, for the delete guard.
    protected abstract Expression<Func<Job, bool>> ReferencedByJob(int id);

    public Task<List<TEntity>> ListAsync(CancellationToken ct = default) =>
        Set.OrderBy(NameSelector).ToListAsync(ct);

    public Task<TEntity?> GetAsync(int id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public Task RemoveAsync(TEntity entity, CancellationToken ct = default)
    {
        Set.Remove(entity);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) =>
        Db.Jobs.AnyAsync(ReferencedByJob(id), ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => Db.SaveChangesAsync(ct);
}
