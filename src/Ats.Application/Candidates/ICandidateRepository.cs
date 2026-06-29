using Ats.Domain.Entities;

namespace Ats.Application.Candidates;

public interface ICandidateRepository
{
    Task<List<Candidate>> ListAsync(CancellationToken ct = default);
    Task<Candidate?> GetAsync(int id, CancellationToken ct = default);
    Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(Candidate candidate, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
