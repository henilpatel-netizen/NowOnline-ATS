using Ats.Application.Departments;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

public sealed class FakeDepartmentRepository : IDepartmentRepository
{
    public List<Department> Departments { get; } = new();
    public HashSet<int> UsedByJob { get; } = new();
    public int SaveCount { get; private set; }

    public Task<List<Department>> ListAsync(CancellationToken ct = default) => Task.FromResult(Departments.ToList());

    public Task<Department?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Departments.FirstOrDefault(d => d.Id == id));

    public Task AddAsync(Department department, CancellationToken ct = default)
    {
        Departments.Add(department);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Department department, CancellationToken ct = default)
    {
        Departments.Remove(department);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) => Task.FromResult(UsedByJob.Contains(id));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
