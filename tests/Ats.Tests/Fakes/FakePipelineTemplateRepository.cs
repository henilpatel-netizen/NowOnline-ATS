using Ats.Application.Pipelines;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

public sealed class FakePipelineTemplateRepository : IPipelineTemplateRepository
{
    public List<PipelineTemplate> Templates { get; } = new();
    public List<PipelineStage> RemovedStages { get; } = new();
    public List<PipelineTemplate> RemovedTemplates { get; } = new();

    public bool ConcurrencyConflict { get; set; }
    public bool UsedByJob { get; set; }
    public int SaveCount { get; private set; }
    public byte[]? ExpectedRowVersion { get; private set; }
    public bool AddCalled { get; private set; }

    public Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(Templates.ToList());

    public Task<PipelineTemplate?> GetWithStagesAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Templates.FirstOrDefault(t => t.Id == id));

    public Task AddAsync(PipelineTemplate template, CancellationToken ct = default)
    {
        AddCalled = true;
        Templates.Add(template);
        return Task.CompletedTask;
    }

    public Task RemoveStagesAsync(IEnumerable<PipelineStage> stages, CancellationToken ct = default)
    {
        var list = stages.ToList();
        RemovedStages.AddRange(list);
        foreach (var t in Templates) foreach (var s in list) t.Stages.Remove(s);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(PipelineTemplate template, CancellationToken ct = default)
    {
        RemovedTemplates.Add(template);
        Templates.Remove(template);
        return Task.CompletedTask;
    }

    public Task<bool> IsUsedByJobAsync(int id, CancellationToken ct = default) => Task.FromResult(UsedByJob);

    public Task<Dictionary<int, int>> JobCountsByTemplateAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<int, int>());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public void SetExpectedRowVersion(PipelineTemplate template, byte[] rowVersion) =>
        ExpectedRowVersion = rowVersion;

    public Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        if (ConcurrencyConflict) return Task.FromResult(false);
        SaveCount++;
        return Task.FromResult(true);
    }
}
