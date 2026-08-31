using Ats.Application.Candidates;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class CandidateRepository : ICandidateRepository
{
    private readonly AtsDbContext _db;
    public CandidateRepository(AtsDbContext db) => _db = db;

    public Task<List<Candidate>> ListAsync(CancellationToken ct = default) =>
        _db.Candidates.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync(ct);


    public Task<Candidate?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Candidates.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Candidates.FirstOrDefaultAsync(c => c.Email == email, ct);

    public async Task AddAsync(Candidate candidate, CancellationToken ct = default) =>
        await _db.Candidates.AddAsync(candidate, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
