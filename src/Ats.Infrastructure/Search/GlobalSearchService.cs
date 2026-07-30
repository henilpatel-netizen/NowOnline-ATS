using Ats.Application.Search;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Search;

public sealed class GlobalSearchService : IGlobalSearchService
{
    private const int PerCategory = 5;
    private const int MinTermLength = 2;

    private readonly AtsDbContext _db;
    public GlobalSearchService(AtsDbContext db) => _db = db;

    public async Task<SearchResults> SearchAsync(string? term, CancellationToken ct = default)
    {
        var trimmed = term?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinTermLength)
            return SearchResults.Empty;

        // The pattern travels as a parameter, so it cannot be injected into SQL. LIKE
        // metacharacters are escaped so a user typing % gets a literal match, not a full scan.
        var pattern = $"%{Escape(trimmed)}%";

        var jobs = await _db.Jobs
            .Where(j => EF.Functions.Like(j.Title, pattern) || EF.Functions.Like(j.ExternalRef, pattern))
            .OrderByDescending(j => j.PublishedAt)
            .Take(PerCategory)
            .Select(j => new JobHit(j.Id, j.Title, j.ExternalRef, j.Status.ToString()))
            .ToListAsync(ct);

        var candidates = await _db.Candidates
            .Where(c => EF.Functions.Like(c.FirstName, pattern)
                     || EF.Functions.Like(c.LastName, pattern)
                     || EF.Functions.Like(c.Email, pattern))
            .OrderBy(c => c.LastName)
            .Take(PerCategory)
            .Select(c => new CandidateHit(c.Id, c.FirstName + " " + c.LastName, c.Email))
            .ToListAsync(ct);

        var applications = await _db.Applications
            .Where(a => a.SourceCode != null && EF.Functions.Like(a.SourceCode, pattern))
            .OrderByDescending(a => a.AppliedAt)
            .Take(PerCategory)
            .Select(a => new ApplicationHit(
                a.Id,
                a.Candidate!.FirstName + " " + a.Candidate.LastName,
                _db.Jobs.Where(j => j.Id == a.JobId).Select(j => j.Title).FirstOrDefault() ?? "",
                a.SourceCode!))
            .ToListAsync(ct);

        return new SearchResults(jobs, candidates, applications);
    }

    private static string Escape(string term) => term
        .Replace("[", "[[]")
        .Replace("%", "[%]")
        .Replace("_", "[_]");
}
