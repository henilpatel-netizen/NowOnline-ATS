using Ats.Application.Jobs;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

public sealed class FakeJobRepository : IJobRepository
{
    public List<Job> Jobs { get; } = new();
    public bool PipelineExists { get; set; } = true;
    public bool NumberClash { get; set; }          // TrySaveChangesAsync fails (unique job number)
    public int NextNumber { get; set; } = 42;
    public int SaveCount { get; private set; }
    public bool AddCalled { get; private set; }

    public Task<List<Job>> ListAsync(CancellationToken ct = default) => Task.FromResult(Jobs.ToList());


    public Task<Job?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Jobs.FirstOrDefault(j => j.Id == id));

    public Task AddAsync(Job job, CancellationToken ct = default)
    {
        AddCalled = true;
        Jobs.Add(job);
        return Task.CompletedTask;
    }

    public Task<int> NextJobNumberAsync(CancellationToken ct = default) => Task.FromResult(NextNumber);

    public Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default) =>
        Task.FromResult(PipelineExists);

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        if (NumberClash) return Task.FromResult(false);
        SaveCount++;
        return Task.FromResult(true);
    }
}
