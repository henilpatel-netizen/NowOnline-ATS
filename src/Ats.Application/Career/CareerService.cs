using Ats.Application.Applications;
using Ats.Application.Candidates;
using Ats.Application.Departments; // OperationResult
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Career;

public sealed class CareerService : ICareerService
{
    private readonly ICareerRepository _career;
    private readonly ICandidateRepository _candidates;
    private readonly IApplicationRepository _applications;
    private readonly IOutboxEnqueuer _outbox;

    public CareerService(ICareerRepository career, ICandidateRepository candidates, IApplicationRepository applications, IOutboxEnqueuer outbox)
    {
        _career = career; _candidates = candidates; _applications = applications; _outbox = outbox;
    }

    public Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default) => _career.GetPublishedJobsAsync(ct);
    public Task<Job?> GetPublishedJobAsync(string externalRef, CancellationToken ct = default) => _career.GetPublishedJobByExternalRefAsync(externalRef, ct);
    public Task<string> GetCodeParameterNameAsync(CancellationToken ct = default) => _career.GetCodeParameterNameAsync(ct);

    public async Task<OperationResult> ApplyAsync(ApplyInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Email)) return OperationResult.Fail("Email is required.");

        var job = await _career.GetPublishedJobByExternalRefAsync(input.ExternalRef, ct);
        if (job is null) return OperationResult.Fail("This job is no longer accepting applications.");

        var stages = await _applications.GetStagesForJobAsync(job.Id, ct);
        var firstStage = stages.OrderBy(s => s.Order).FirstOrDefault();
        if (firstStage is null) return OperationResult.Fail("This job is not accepting applications yet.");

        var email = input.Email.Trim().ToLowerInvariant();
        var candidate = await _candidates.GetByEmailAsync(email, ct);
        if (candidate is null)
        {
            candidate = new Candidate
            {
                FirstName = input.FirstName.Trim(), LastName = input.LastName.Trim(),
                Email = email, Phone = input.Phone?.Trim(), ResumeFileKey = input.ResumeFileKey
            };
            await _candidates.AddAsync(candidate, ct);
            await _candidates.SaveChangesAsync(ct); // assigns candidate.Id
        }
        else
        {
            candidate.FirstName = input.FirstName.Trim();
            candidate.LastName = input.LastName.Trim();
            if (!string.IsNullOrWhiteSpace(input.Phone)) candidate.Phone = input.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(input.ResumeFileKey)) candidate.ResumeFileKey = input.ResumeFileKey;
        }

        var code = string.IsNullOrWhiteSpace(input.SourceCode) ? null : input.SourceCode.Trim();
        if (code is { Length: > 36 }) code = code[..36];

        var existing = await _applications.FindByCandidateJobAsync(candidate.Id, job.Id, ct);
        if (existing is not null)
        {
            if (code is not null) existing.SourceCode = code;   // re-apply: refresh code, no duplicate
            await _candidates.SaveChangesAsync(ct);             // persists candidate + existing changes
            return OperationResult.Ok;
        }

        var application = new JobApplication
        {
            CandidateId = candidate.Id,
            JobId = job.Id,
            CurrentStageId = firstStage.Id,
            SourceCode = code,
            AppliedAt = DateTimeOffset.UtcNow,
            Status = ApplicationStatus.Active
        };
        await _applications.AddApplicationAsync(application, ct);
        await _applications.SaveChangesAsync(ct);

        await _applications.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = null   // public apply, no signed-in user
        }, ct);
        await _outbox.StageAsync(application.Id, firstStage.Id, ct);
        await _applications.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
