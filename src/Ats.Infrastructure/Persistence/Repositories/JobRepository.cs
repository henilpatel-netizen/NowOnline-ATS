using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class JobRepository : IJobRepository
{
    private readonly AtsDbContext _db;
    public JobRepository(AtsDbContext db) => _db = db;

    public Task<List<Job>> ListAsync(CancellationToken ct = default) =>
        _db.Jobs.OrderByDescending(j => j.Id).ToListAsync(ct);

    public async Task<(List<Job> Jobs, int Total)> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Jobs.AsQueryable();
        if (status is not null) query = query.Where(j => j.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(j => j.Title.Contains(s) || j.ExternalRef.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var jobs = await query.OrderByDescending(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (jobs, total);
    }

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
