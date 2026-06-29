using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class JobRepository : IJobRepository
{
    private readonly AtsDbContext _db;
    public JobRepository(AtsDbContext db) => _db = db;

    public Task<List<Job>> ListAsync(CancellationToken ct = default) =>
        _db.Jobs.OrderByDescending(j => j.Id).ToListAsync(ct);

    public Task<Job?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task AddAsync(Job job, CancellationToken ct = default) =>
        await _db.Jobs.AddAsync(job, ct);

    // Increments the current tenant's LastJobNumber and returns the new value.
    public async Task<int> NextJobNumberAsync(CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct); // tenant-filtered to the current tenant
        settings.LastJobNumber += 1;
        return settings.LastJobNumber;
    }

    public Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default) =>
        _db.PipelineTemplates.AnyAsync(t => t.Id == pipelineTemplateId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
