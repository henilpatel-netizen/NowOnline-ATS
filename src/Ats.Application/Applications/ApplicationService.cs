using Ats.Application.Abstractions;
using Ats.Application.Candidates;
using Ats.Application.Departments; // OperationResult
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Applications;

public interface IApplicationService
{
    Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default);
    Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default);
    Task<List<JobApplication>> ListForJobAsync(int jobId, CancellationToken ct = default);
    Task<JobApplication?> GetAsync(int id, CancellationToken ct = default);
    Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default);
    Task<OperationResult> AddCandidateToJobAsync(AddCandidateToJobInput input, CancellationToken ct = default);
    Task<OperationResult> AddExistingCandidateToJobAsync(int jobId, int candidateId, CancellationToken ct = default);
    Task<OperationResult> MoveStageAsync(int applicationId, int toStageId, byte[] rowVersion, CancellationToken ct = default);
}

public sealed class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _repo;
    private readonly ICandidateRepository _candidates;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxEnqueuer _outbox;

    public ApplicationService(IApplicationRepository repo, ICandidateRepository candidates, ICurrentUser currentUser, IOutboxEnqueuer outbox)
    {
        _repo = repo; _candidates = candidates; _currentUser = currentUser; _outbox = outbox;
    }

    public Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default) => _repo.GetJobAsync(jobId, ct);
    public Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default) => _repo.GetStagesForJobAsync(jobId, ct);
    public Task<List<JobApplication>> ListForJobAsync(int jobId, CancellationToken ct = default) => _repo.ListForJobAsync(jobId, ct);
    public Task<JobApplication?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);
    public Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default) => _repo.ListEventsAsync(applicationId, ct);

    public async Task<OperationResult> AddCandidateToJobAsync(AddCandidateToJobInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Email)) return OperationResult.Fail("Email is required.");

        var email = input.Email.Trim().ToLowerInvariant();
        var candidate = await _candidates.GetByEmailAsync(email, ct);
        if (candidate is null)
        {
            candidate = new Candidate
            {
                FirstName = input.FirstName.Trim(), LastName = input.LastName.Trim(),
                Email = email, Phone = input.Phone?.Trim()
            };
            await _candidates.AddAsync(candidate, ct);
            await _candidates.SaveChangesAsync(ct);   // assigns candidate.Id
        }

        return await CreateApplicationAsync(candidate, input.JobId, ct);
    }

    public async Task<OperationResult> AddExistingCandidateToJobAsync(int jobId, int candidateId, CancellationToken ct = default)
    {
        var candidate = await _candidates.GetAsync(candidateId, ct);
        if (candidate is null) return OperationResult.Fail("Candidate not found.");
        return await CreateApplicationAsync(candidate, jobId, ct);
    }

    // Shared by both add paths: validates the job + first stage, dedupes, and creates the
    // application plus its initial ApplicationEvent.
    private async Task<OperationResult> CreateApplicationAsync(Candidate candidate, int jobId, CancellationToken ct)
    {
        var job = await _repo.GetJobAsync(jobId, ct);
        if (job is null) return OperationResult.Fail("Job not found.");

        var stages = await _repo.GetStagesForJobAsync(jobId, ct);
        var firstStage = stages.OrderBy(s => s.Order).FirstOrDefault();
        if (firstStage is null) return OperationResult.Fail("This job's pipeline has no stages.");

        var existing = await _repo.FindByCandidateJobAsync(candidate.Id, jobId, ct);
        if (existing is not null) return OperationResult.Ok;  // already applied; no duplicate

        var application = new JobApplication
        {
            CandidateId = candidate.Id,
            JobId = jobId,
            CurrentStageId = firstStage.Id,
            AppliedAt = DateTimeOffset.UtcNow,
            Status = ApplicationStatus.Active
        };
        await _repo.AddApplicationAsync(application, ct);
        await _repo.SaveChangesAsync(ct);   // assigns application.Id

        await _repo.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = _currentUser.UserId
        }, ct);
        await _outbox.StageAsync(application.Id, firstStage.Id, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> MoveStageAsync(int applicationId, int toStageId, byte[] rowVersion, CancellationToken ct = default)
    {
        var application = await _repo.GetAsync(applicationId, ct);
        if (application is null) return OperationResult.Fail("Application not found.");

        var stages = await _repo.GetStagesForJobAsync(application.JobId, ct);
        var target = stages.FirstOrDefault(s => s.Id == toStageId);
        if (target is null) return OperationResult.Fail("That stage does not belong to this job's pipeline.");
        if (application.CurrentStageId == toStageId) return OperationResult.Ok; // no-op

        var fromStageId = application.CurrentStageId;
        application.CurrentStageId = toStageId;
        application.Status = target.IsTerminal
            ? (target.TerminalOutcome == StageOutcome.Hired ? ApplicationStatus.Hired
               : target.TerminalOutcome == StageOutcome.Rejected ? ApplicationStatus.Rejected
               : ApplicationStatus.Active)
            : ApplicationStatus.Active;

        await _repo.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = fromStageId,
            ToStageId = toStageId,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = _currentUser.UserId
        }, ct);

        await _outbox.StageAsync(application.Id, toStageId, ct);

        _repo.SetExpectedRowVersion(application, rowVersion);
        var ok = await _repo.TrySaveChangesAsync(ct);
        return ok ? OperationResult.Ok
                  : OperationResult.Fail("This application was changed by someone else. Reload the board and try again.");
    }
}
