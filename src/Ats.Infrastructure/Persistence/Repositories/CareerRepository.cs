using Ats.Application.Career;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class CareerRepository : ICareerRepository
{
    private readonly AtsDbContext _db;
    public CareerRepository(AtsDbContext db) => _db = db;

    public Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.Department).Include(j => j.Location)
            .Where(j => j.Status == JobStatus.Published)
            .OrderByDescending(j => j.PublishedAt)
            .ToListAsync(ct);

    public Task<Job?> GetPublishedJobByExternalRefAsync(string externalRef, CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.Department).Include(j => j.Location)
            .FirstOrDefaultAsync(j => j.ExternalRef == externalRef && j.Status == JobStatus.Published, ct);

    public async Task<string> GetCodeParameterNameAsync(CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(settings?.CodeParameterName) ? "ref" : settings!.CodeParameterName;
    }
}
