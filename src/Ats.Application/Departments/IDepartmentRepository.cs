using Ats.Domain.Entities;

namespace Ats.Application.Departments;

public interface IDepartmentRepository
{
    Task<List<Department>> ListAsync(CancellationToken ct = default);
    Task<Department?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Department department, CancellationToken ct = default);
    Task RemoveAsync(Department department, CancellationToken ct = default);
    Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
