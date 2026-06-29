using Ats.Application.Departments;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository : IDepartmentRepository
{
    private readonly AtsDbContext _db;
    public DepartmentRepository(AtsDbContext db) => _db = db;

    public Task<List<Department>> ListAsync(CancellationToken ct = default) =>
        _db.Departments.OrderBy(d => d.Name).ToListAsync(ct);

    public Task<Department?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(Department department, CancellationToken ct = default) =>
        await _db.Departments.AddAsync(department, ct);

    public Task RemoveAsync(Department department, CancellationToken ct = default)
    {
        _db.Departments.Remove(department);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.AnyAsync(j => j.DepartmentId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
