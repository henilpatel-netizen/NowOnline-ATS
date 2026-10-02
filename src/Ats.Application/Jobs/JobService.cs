using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

public interface IJobService
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    // Scoped to the jobs the user may see; null when out of scope, exactly as for a missing id.
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task<JobSaveResult> CreateAsync(JobInput input, CancellationToken ct = default);
    Task<JobSaveResult> UpdateAsync(JobInput input, CancellationToken ct = default);
    Task<OperationResult> PublishAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CloseAsync(int id, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class JobService : IJobService
{
    private readonly IJobRepository _repo;
    private readonly IOutboxEnqueuer _outbox;
    private readonly IJobScope _scope;
    public JobService(IJobRepository repo, IOutboxEnqueuer outbox, IJobScope scope)
    {
        _repo = repo; _outbox = outbox; _scope = scope;
    }

    public Task<List<Job>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<Job?> GetAsync(int id, CancellationToken ct = default)
    {
        return await _scope.AllowsAsync(userId => _repo.IsAssignedToAsync(id, userId, ct))
            ? await _repo.GetAsync(id, ct)
            : null;
    }

    public async Task<JobSaveResult> CreateAsync(JobInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return JobSaveResult.Failed("Title is required.");
        if (!await _repo.PipelineExistsAsync(input.PipelineTemplateId, ct))
            return JobSaveResult.Failed("Select a valid pipeline template.");
        var team = await AssignableTeamAsync(input.HiringManagerIds, ct);
        if (team is null) return JobSaveResult.Failed(TeamRejected);

        var number = await _repo.NextJobNumberAsync(ct);
        var job = new Job
        {
            Title = input.Title.Trim(),
            Description = input.Description,
            DepartmentId = input.DepartmentId,
            LocationId = input.LocationId,
            EmploymentType = input.EmploymentType,
            PipelineTemplateId = input.PipelineTemplateId,
            Status = JobStatus.Draft,
            ExternalRef = $"JOB-{number}"
        };
        var change = await ReplaceTeamAsync(job, team, ct);
        await _repo.AddAsync(job, ct);
        return await _repo.TrySaveNewJobAsync(ct)
            ? JobSaveResult.Saved(change)
            : JobSaveResult.Failed("Could not assign a job number just now. Please try again.");
    }

    public async Task<JobSaveResult> UpdateAsync(JobInput input, CancellationToken ct = default)
    {
        if (input.Id is not int id) return JobSaveResult.Failed("Missing job id.");
        if (string.IsNullOrWhiteSpace(input.Title)) return JobSaveResult.Failed("Title is required.");
        // Own scope check, so editing does not rely on jobs.manage implying jobs.viewall.
        var job = await _scope.AllowsAsync(userId => _repo.IsAssignedToAsync(id, userId, ct))
            ? await _repo.GetWithHiringManagersAsync(id, ct)
            : null;
        if (job is null) return JobSaveResult.Failed("Job not found.");
        if (!await _repo.PipelineExistsAsync(input.PipelineTemplateId, ct))
            return JobSaveResult.Failed("Select a valid pipeline template.");
        var team = await AssignableTeamAsync(input.HiringManagerIds, ct);
        if (team is null) return JobSaveResult.Failed(TeamRejected);

        job.Title = input.Title.Trim();
        job.Description = input.Description;
        job.DepartmentId = input.DepartmentId;
        job.LocationId = input.LocationId;
        job.EmploymentType = input.EmploymentType;
        job.PipelineTemplateId = input.PipelineTemplateId;
        var change = await ReplaceTeamAsync(job, team, ct);
        if (job.PublishedAt is not null) await _outbox.StageVacancySyncAsync(job, ct);
        // A concurrent save adding the same manager hits the unique (job, user) index.
        if (!await _repo.TrySaveTeamChangesAsync(ct))
            return JobSaveResult.Failed(TeamSaveFailed);
        return JobSaveResult.Saved(change);
    }

    private const string TeamSaveFailed = "The job could not be saved just now. Reload the page and try again.";

    // Same message whether an id is another tenant's, missing, deactivated or not a hiring manager, so
    // the response never reveals which ids exist.
    private const string TeamRejected = "One or more of the selected hiring managers cannot be assigned. Reload the page and choose again.";

    // The de-duplicated ids, or null when any of them is not an active HiringManager in this tenant.
    private async Task<IReadOnlyList<int>?> AssignableTeamAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return wanted;
        var found = (await _repo.FindAssignableHiringManagerIdsAsync(wanted, ct)).ToHashSet();
        return wanted.All(found.Contains) ? wanted : null;
    }

    // Links are added through the job's collection (so JobId comes from the loaded job and TenantId from
    // the interceptor) and removed by comparing against that loaded collection, never by a posted id.
    private async Task<HiringTeamChange> ReplaceTeamAsync(Job job, IReadOnlyList<int> team, CancellationToken ct)
    {
        var wanted = team.ToHashSet();
        var current = job.HiringManagers.Select(h => h.UserId).ToHashSet();
        var removed = job.HiringManagers.Where(h => !wanted.Contains(h.UserId)).ToList();
        var added = team.Where(id => !current.Contains(id)).ToList();
        if (removed.Count == 0 && added.Count == 0) return HiringTeamChange.None;

        foreach (var link in removed) job.HiringManagers.Remove(link);
        foreach (var userId in added) job.HiringManagers.Add(new JobHiringManager { UserId = userId });

        var names = await _repo.GetUserNamesAsync([.. added, .. removed.Select(h => h.UserId)], ct);
        List<string> Named(IEnumerable<int> ids) =>
            ids.Select(id => names.TryGetValue(id, out var n) ? n : "a former user").Order(StringComparer.CurrentCulture).ToList();
        return new HiringTeamChange(Named(added), Named(removed.Select(h => h.UserId)));
    }

    public async Task<OperationResult> PublishAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (job.Status == JobStatus.Published) return OperationResult.Fail("Job is already published.");
        job.Status = JobStatus.Published;
        job.PublishedAt ??= DateTimeOffset.UtcNow;
        await _outbox.StageVacancySyncAsync(job, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> CloseAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (job.Status != JobStatus.Published) return OperationResult.Fail("Only a published job can be closed.");
        job.Status = JobStatus.Closed;
        await _outbox.StageVacancySyncAsync(job, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        job.IsDeleted = true;   // soft delete
        if (job.PublishedAt is not null) await _outbox.StageVacancySyncAsync(job, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
