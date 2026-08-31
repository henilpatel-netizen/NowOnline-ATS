using Ats.Application.Common;
using Ats.Domain.Entities;

namespace Ats.Application.Departments;

public interface IDepartmentService
{
    Task<List<Department>> ListAsync(CancellationToken ct = default);
    Task<Department?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(string name, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(int id, string name, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _repo;
    public DepartmentService(IDepartmentRepository repo) => _repo = repo;

    public Task<List<Department>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Department?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        await _repo.AddAsync(new Department { Name = name.Trim() }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(int id, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        var dept = await _repo.GetAsync(id, ct);
        if (dept is null) return OperationResult.Fail("Department not found.");
        dept.Name = name.Trim();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var dept = await _repo.GetAsync(id, ct);
        if (dept is null) return OperationResult.Fail("Department not found.");
        if (await _repo.IsReferencedByJobAsync(id, ct))
            return OperationResult.Fail("This department is used by one or more jobs and cannot be deleted.");
        await _repo.RemoveAsync(dept, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
