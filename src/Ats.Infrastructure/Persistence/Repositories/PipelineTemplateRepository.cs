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

    public async Task<Dictionary<int, int>> JobCountsByTemplateAsync(CancellationToken ct = default) =>
        await _db.Jobs.GroupBy(j => j.PipelineTemplateId)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TemplateId, x => x.Count, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public void SetExpectedRowVersion(PipelineTemplate template, byte[] rowVersion)
    {
        var entry = _db.Entry(template);
        entry.Property(t => t.RowVersion).OriginalValue = rowVersion;
        // Force an UPDATE on the template row even when only child stages changed, so the token is
        // always checked and regenerated (a stage-only edit still detects a concurrent change).
        entry.Property(t => t.Name).IsModified = true;
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }
}
