using Ats.Application.Pipelines;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class PipelineTemplateRepository : IPipelineTemplateRepository
{
    private readonly AtsDbContext _db;
    public PipelineTemplateRepository(AtsDbContext db) => _db = db;

    public Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default) =>
        _db.PipelineTemplates.Include(t => t.Stages).OrderBy(t => t.Name).ToListAsync(ct);

    public Task<PipelineTemplate?> GetWithStagesAsync(int id, CancellationToken ct = default) =>
        _db.PipelineTemplates.Include(t => t.Stages).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(PipelineTemplate template, CancellationToken ct = default) =>
        await _db.PipelineTemplates.AddAsync(template, ct);

    public Task RemoveStagesAsync(IEnumerable<PipelineStage> stages, CancellationToken ct = default)
    {
        _db.PipelineStages.RemoveRange(stages);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(PipelineTemplate template, CancellationToken ct = default)
    {
        _db.PipelineStages.RemoveRange(template.Stages);
        _db.PipelineTemplates.Remove(template);
        return Task.CompletedTask;
    }

    public Task<bool> IsUsedByJobAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.AnyAsync(j => j.PipelineTemplateId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
