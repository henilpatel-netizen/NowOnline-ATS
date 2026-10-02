using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Tests.Fakes;

public sealed class FakeJobRepository : IJobRepository
{
    public List<Job> Jobs { get; } = new();
    public bool PipelineExists { get; set; } = true;
    public bool RejectNextCreateAsDuplicateJobNumber { get; set; }   // TrySaveNewJobAsync hits the job number index
    public Exception? CreateFailure { get; set; }                     // TrySaveNewJobAsync throws this instead
    public bool RejectNextSaveAsDuplicateTeamLink { get; set; }   // TrySaveTeamChangesAsync hits the (job, user) unique index
    public int NextNumber { get; set; } = 42;
    public int SaveCount { get; private set; }
    public bool AddCalled { get; private set; }
    // The current tenant's users, as the filtered context sees them: an id not here belongs to another tenant or does not exist.
    public Dictionary<int, FakeTeamUser> Users { get; } = new();
    public int AssignableQueries { get; private set; }

    public Task<List<Job>> ListAsync(CancellationToken ct = default) => Task.FromResult(Jobs.ToList());


    public Task<Job?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Jobs.FirstOrDefault(j => j.Id == id));

    public Task<Job?> GetWithHiringManagersAsync(int id, CancellationToken ct = default) => GetAsync(id, ct);

    public Task<IReadOnlyList<int>> FindAssignableHiringManagerIdsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        AssignableQueries++;
        IReadOnlyList<int> found = userIds
            .Where(id => Users.TryGetValue(id, out var u) && u.IsActive && u.Role == AtsRole.HiringManager)
            .ToList();
        return Task.FromResult(found);
    }

    public Task<IReadOnlyDictionary<int, string>> GetUserNamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        IReadOnlyDictionary<int, string> names = userIds.Where(Users.ContainsKey).ToDictionary(id => id, id => Users[id].Name);
        return Task.FromResult(names);
    }

    public int AssignedQueries { get; private set; }

    public Task<bool> IsAssignedToAsync(int jobId, int userId, CancellationToken ct = default)
    {
        AssignedQueries++;
        return Task.FromResult(Jobs.Any(j => j.Id == jobId && !j.IsDeleted && j.HiringManagers.Any(h => h.UserId == userId)));
    }

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

    public Task<bool> TrySaveNewJobAsync(CancellationToken ct = default)
    {
        if (CreateFailure is not null) throw CreateFailure;
        if (RejectNextCreateAsDuplicateJobNumber) return Task.FromResult(false);
        SaveCount++;
        return Task.FromResult(true);
    }

    public Task<bool> TrySaveTeamChangesAsync(CancellationToken ct = default)
    {
        if (RejectNextSaveAsDuplicateTeamLink)
        {
            RejectNextSaveAsDuplicateTeamLink = false;
            return Task.FromResult(false);
        }
        SaveCount++;
        return Task.FromResult(true);
    }
}

public sealed record FakeTeamUser(string Name, bool IsActive = true, string Role = AtsRole.HiringManager);
