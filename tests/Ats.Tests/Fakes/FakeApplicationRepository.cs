using Ats.Application.Abstractions;
using Ats.Application.Applications;
using Ats.Application.Candidates;
using Ats.Application.Integration;
using Ats.Domain.Entities;

namespace Ats.Tests.Fakes;

// Hand-rolled fakes. The Application layer depends only on these interfaces, so its rules are
// testable with no database and no mocking framework.
public sealed class FakeApplicationRepository : IApplicationRepository
{
    public List<Job> Jobs { get; } = new();
    public List<PipelineStage> Stages { get; } = new();
    public List<JobApplication> Applications { get; } = new();
    public List<ApplicationEvent> Events { get; } = new();

    // Set to simulate another writer winning the race (TrySaveChangesAsync returns false).
    public bool ConcurrencyConflict { get; set; }

    public int SaveCount { get; private set; }
    public int TransactionCount { get; private set; }
    public byte[]? ExpectedRowVersion { get; private set; }
    // Records how many saves happened inside the transaction, to prove the unit of work is atomic.
    public int SavesInsideTransaction { get; private set; }

    private bool _inTransaction;
    private int _nextApplicationId = 1;

    public Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default) =>
        Task.FromResult(Jobs.FirstOrDefault(j => j.Id == jobId));

    public Task<Dictionary<int, DateTimeOffset>> LatestEventTimesForJobAsync(int jobId, CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<int, DateTimeOffset>());

    public Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default)
    {
        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return Task.FromResult(new List<PipelineStage>());
        return Task.FromResult(Stages.Where(s => s.PipelineTemplateId == job.PipelineTemplateId)
                                     .OrderBy(s => s.Order).ToList());
    }

    public Task<List<JobApplication>> ListForJobAsync(int jobId, CancellationToken ct = default) =>
        Task.FromResult(Applications.Where(a => a.JobId == jobId).ToList());

    public Task<JobApplication?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Applications.FirstOrDefault(a => a.Id == id));

    public Task<JobApplication?> GetWithCandidateAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Applications.FirstOrDefault(a => a.Id == id));

    public Task<JobApplication?> FindByCandidateJobAsync(int candidateId, int jobId, CancellationToken ct = default) =>
        Task.FromResult(Applications.FirstOrDefault(a => a.CandidateId == candidateId && a.JobId == jobId));

    public Task AddApplicationAsync(JobApplication application, CancellationToken ct = default)
    {
        if (application.Id == 0) application.Id = _nextApplicationId++;
        Applications.Add(application);
        return Task.CompletedTask;
    }

    public Task AddEventAsync(ApplicationEvent ev, CancellationToken ct = default)
    {
        Events.Add(ev);
        return Task.CompletedTask;
    }

    public Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default) =>
        Task.FromResult(Events.Where(e => e.ApplicationId == applicationId).ToList());

    public void SetExpectedRowVersion(JobApplication application, byte[] rowVersion) =>
        ExpectedRowVersion = rowVersion;

    public Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        if (ConcurrencyConflict) return Task.FromResult(false);
        Save();
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        Save();
        return Task.CompletedTask;
    }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        TransactionCount++;
        _inTransaction = true;
        try { return await work(ct); }
        finally { _inTransaction = false; }
    }

    private void Save()
    {
        SaveCount++;
        if (_inTransaction) SavesInsideTransaction++;
    }
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public int? UserId { get; set; } = 7;
    public string? Name { get; set; } = "Test User";
    public string? Role { get; set; } = "Owner";
    public bool IsAuthenticated => UserId is not null;
}

public sealed class FakeOutboxEnqueuer : IOutboxEnqueuer
{
    public List<(int ApplicationId, int ToStageId)> Staged { get; } = new();

    public Task StageAsync(int applicationId, int toStageId, CancellationToken ct = default)
    {
        Staged.Add((applicationId, toStageId));
        return Task.CompletedTask;
    }
}

public sealed class FakeCandidateRepository : ICandidateRepository
{
    public List<Candidate> Candidates { get; } = new();
    public int SaveCount { get; private set; }
    private int _nextId = 100;

    public Task<List<Candidate>> ListAsync(CancellationToken ct = default) => Task.FromResult(Candidates.ToList());


    public Task<Candidate?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Candidates.FirstOrDefault(c => c.Id == id));

    public Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Candidates.FirstOrDefault(c => c.Email == email));

    public Task AddAsync(Candidate candidate, CancellationToken ct = default)
    {
        if (candidate.Id == 0) candidate.Id = _nextId++;
        Candidates.Add(candidate);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
