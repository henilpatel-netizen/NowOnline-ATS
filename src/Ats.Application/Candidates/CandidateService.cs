using Ats.Application.Common;
using Ats.Application.Jobs;
using Ats.Domain.Entities;

namespace Ats.Application.Candidates;

public interface ICandidateService
{
    Task<List<Candidate>> ListAsync(CancellationToken ct = default);
    // Scoped: a restricted user sees only candidates with an application on one of their jobs;
    // null when out of scope, exactly as for a missing id.
    Task<Candidate?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(string firstName, string lastName, string email, string? phone, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(int id, string firstName, string lastName, string email, string? phone, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class CandidateService : ICandidateService
{
    private readonly ICandidateRepository _repo;
    private readonly IJobScope _scope;
    public CandidateService(ICandidateRepository repo, IJobScope scope)
    {
        _repo = repo; _scope = scope;
    }

    public Task<List<Candidate>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Candidate?> GetAsync(int id, CancellationToken ct = default)
    {
        return await _scope.AllowsAsync(userId => _repo.HasApplicationOnJobAssignedToAsync(id, userId, ct))
            ? await _repo.GetAsync(id, ct)
            : null;
    }

    public async Task<OperationResult> CreateAsync(string firstName, string lastName, string email, string? phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return OperationResult.Fail("Email is required.");
        var normalized = email.Trim().ToLowerInvariant();
        if (await _repo.GetByEmailAsync(normalized, ct) is not null)
            return OperationResult.Fail("A candidate with this email already exists.");
        await _repo.AddAsync(new Candidate
        {
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = normalized,
            Phone = phone?.Trim()
        }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(int id, string firstName, string lastName, string email, string? phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return OperationResult.Fail("Email is required.");
        var candidate = await _repo.GetAsync(id, ct);
        if (candidate is null) return OperationResult.Fail("Candidate not found.");
        var normalized = email.Trim().ToLowerInvariant();
        var byEmail = await _repo.GetByEmailAsync(normalized, ct);
        if (byEmail is not null && byEmail.Id != id)
            return OperationResult.Fail("Another candidate already uses this email.");
        candidate.FirstName = firstName.Trim();
        candidate.LastName = lastName.Trim();
        candidate.Email = normalized;
        candidate.Phone = phone?.Trim();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var candidate = await _repo.GetAsync(id, ct);
        if (candidate is null) return OperationResult.Fail("Candidate not found.");
        candidate.IsDeleted = true;
        foreach (var application in await _repo.ListApplicationsAsync(id, ct))
            application.IsDeleted = true;
        // Applications carry a RowVersion, so a board move between load and save is a conflict.
        return await _repo.TrySaveChangesAsync(ct)
            ? OperationResult.Ok
            : OperationResult.Fail("This candidate was changed by someone else. Reload the page and try again.");
    }
}
