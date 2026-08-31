using Ats.Application.Common;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

public interface IJobService
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(JobInput input, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(JobInput input, CancellationToken ct = default);
    Task<OperationResult> PublishAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CloseAsync(int id, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class JobService : IJobService
{
    private readonly IJobRepository _repo;
    public JobService(IJobRepository repo) => _repo = repo;

    public Task<List<Job>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public Task<Job?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(JobInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return OperationResult.Fail("Title is required.");
        if (!await _repo.PipelineExistsAsync(input.PipelineTemplateId, ct))
            return OperationResult.Fail("Select a valid pipeline template.");

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
        await _repo.AddAsync(job, ct);
        if (!await _repo.TrySaveChangesAsync(ct))
            return OperationResult.Fail("Could not assign a job number just now. Please try again.");
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(JobInput input, CancellationToken ct = default)
    {
        if (input.Id is not int id) return OperationResult.Fail("Missing job id.");
        if (string.IsNullOrWhiteSpace(input.Title)) return OperationResult.Fail("Title is required.");
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (!await _repo.PipelineExistsAsync(input.PipelineTemplateId, ct))
            return OperationResult.Fail("Select a valid pipeline template.");

        job.Title = input.Title.Trim();
        job.Description = input.Description;
        job.DepartmentId = input.DepartmentId;
        job.LocationId = input.LocationId;
        job.EmploymentType = input.EmploymentType;
        job.PipelineTemplateId = input.PipelineTemplateId;
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> PublishAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (job.Status == JobStatus.Published) return OperationResult.Fail("Job is already published.");
        job.Status = JobStatus.Published;
        job.PublishedAt ??= DateTimeOffset.UtcNow;
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> CloseAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (job.Status != JobStatus.Published) return OperationResult.Fail("Only a published job can be closed.");
        job.Status = JobStatus.Closed;
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        job.IsDeleted = true;   // soft delete
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
