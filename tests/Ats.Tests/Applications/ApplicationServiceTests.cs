using Ats.Application.Applications;
using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Applications;

// The stage-move rules and the create unit of work are the highest-risk logic in the product and
// previously had no coverage at all (QUAL-1).
public class ApplicationServiceTests
{
    private const int JobId = 1;
    private const int TemplateId = 10;

    // Applied -> Interview -> Hired(terminal) / Rejected(terminal)
    private const int Applied = 100;
    private const int Interview = 101;
    private const int Hired = 102;
    private const int Rejected = 103;
    private const int OnHold = 104;   // terminal flag set, but outcome None

    private static (ApplicationService Service, FakeApplicationRepository Repo,
                    FakeCandidateRepository Candidates, FakeOutboxEnqueuer Outbox) Build(FakeCurrentUser? user = null)
    {
        user ??= new FakeCurrentUser();
        var repo = new FakeApplicationRepository();
        repo.Jobs.Add(new Job { Id = JobId, Title = "Dev", PipelineTemplateId = TemplateId, ExternalRef = "JOB-1" });
        repo.Stages.AddRange(new[]
        {
            new PipelineStage { Id = Applied,   PipelineTemplateId = TemplateId, Name = "Applied",   Order = 1 },
            new PipelineStage { Id = Interview, PipelineTemplateId = TemplateId, Name = "Interview", Order = 2 },
            new PipelineStage { Id = Hired,     PipelineTemplateId = TemplateId, Name = "Hired",     Order = 3, IsTerminal = true, TerminalOutcome = StageOutcome.Hired },
            new PipelineStage { Id = Rejected,  PipelineTemplateId = TemplateId, Name = "Rejected",  Order = 4, IsTerminal = true, TerminalOutcome = StageOutcome.Rejected },
            new PipelineStage { Id = OnHold,    PipelineTemplateId = TemplateId, Name = "On hold",   Order = 5, IsTerminal = true, TerminalOutcome = StageOutcome.None },
        });
        var candidates = new FakeCandidateRepository();
        var outbox = new FakeOutboxEnqueuer();
        var service = new ApplicationService(repo, candidates, user, outbox, new JobScope(user));
        return (service, repo, candidates, outbox);
    }

    private static JobApplication SeedApplication(FakeApplicationRepository repo, int stageId = Applied)
    {
        var app = new JobApplication
        {
            Id = 500,
            CandidateId = 900,
            JobId = JobId,
            CurrentStageId = stageId,
            Status = ApplicationStatus.Active,
            AppliedAt = DateTimeOffset.UtcNow,
            RowVersion = new byte[] { 1, 2, 3 }
        };
        repo.Applications.Add(app);
        return app;
    }

    // ---- Stage moves: terminal-outcome mapping -------------------------------------------------

    [Fact]
    public async Task Moving_to_a_hired_stage_marks_the_application_hired()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);

        var result = await service.MoveStageAsync(JobId, app.Id, Hired, app.RowVersion);

        Assert.True(result.Succeeded);
        Assert.Equal(ApplicationStatus.Hired, app.Status);
        Assert.Equal(Hired, app.CurrentStageId);
    }

    [Fact]
    public async Task Moving_to_a_rejected_stage_marks_the_application_rejected()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);

        var result = await service.MoveStageAsync(JobId, app.Id, Rejected, app.RowVersion);

        Assert.True(result.Succeeded);
        Assert.Equal(ApplicationStatus.Rejected, app.Status);
    }

    [Fact]
    public async Task A_terminal_stage_with_no_outcome_leaves_the_application_active()
    {
        // "On hold" is terminal but carries no outcome: the application must not be closed.
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);

        await service.MoveStageAsync(JobId, app.Id, OnHold, app.RowVersion);

        Assert.Equal(ApplicationStatus.Active, app.Status);
        Assert.Equal(OnHold, app.CurrentStageId);
    }

    [Fact]
    public async Task Moving_back_to_a_non_terminal_stage_reopens_the_application()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo, Hired);
        app.Status = ApplicationStatus.Hired;

        await service.MoveStageAsync(JobId, app.Id, Interview, app.RowVersion);

        Assert.Equal(ApplicationStatus.Active, app.Status);
    }

    // ---- Stage moves: guards ------------------------------------------------------------------

    [Fact]
    public async Task Moving_to_the_current_stage_is_a_no_op_and_records_nothing()
    {
        var (service, repo, _, outbox) = Build();
        var app = SeedApplication(repo);

        var result = await service.MoveStageAsync(JobId, app.Id, Applied, app.RowVersion);

        Assert.True(result.Succeeded);
        Assert.Empty(repo.Events);
        Assert.Empty(outbox.Staged);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Moving_to_a_stage_from_another_pipeline_is_rejected()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);

        var result = await service.MoveStageAsync(JobId, app.Id, 9999, app.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Contains("does not belong", result.Error);
        Assert.Equal(Applied, app.CurrentStageId);
    }

    [Fact]
    public async Task Moving_an_unknown_application_is_rejected()
    {
        var (service, _, _, _) = Build();

        var result = await service.MoveStageAsync(JobId, 404, Hired, new byte[] { 1 });

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task A_concurrent_edit_is_reported_and_not_silently_overwritten()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);
        repo.ConcurrencyConflict = true;

        var result = await service.MoveStageAsync(JobId, app.Id, Hired, app.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Contains("changed by someone else", result.Error);
    }

    [Fact]
    public async Task The_move_pins_the_row_version_supplied_by_the_caller()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);
        var expected = new byte[] { 9, 9, 9 };

        await service.MoveStageAsync(JobId, app.Id, Interview, expected);

        Assert.Equal(expected, repo.ExpectedRowVersion);
    }

    [Fact]
    public async Task A_move_records_an_event_and_stages_the_referral_update()
    {
        var (service, repo, _, outbox) = Build();
        var app = SeedApplication(repo);

        await service.MoveStageAsync(JobId, app.Id, Interview, app.RowVersion);

        var ev = Assert.Single(repo.Events);
        Assert.Equal(Applied, ev.FromStageId);
        Assert.Equal(Interview, ev.ToStageId);
        Assert.Equal(7, ev.MovedByUserId);              // from FakeCurrentUser
        Assert.Equal((app.Id, Interview), Assert.Single(outbox.Staged));
    }

    [Theory]
    [InlineData(AtsRole.Owner)]
    [InlineData(AtsRole.Recruiter)]
    [InlineData(AtsRole.HiringManager)]
    [InlineData(AtsRole.Viewer)]
    public async Task A_move_whose_route_job_is_not_the_application_job_is_rejected(string role)
    {
        var user = new FakeCurrentUser { Role = role };
        var (service, repo, _, outbox) = Build(user);
        var app = SeedApplication(repo);
        const int otherJob = 2;
        repo.Jobs.Add(new Job { Id = otherJob, Title = "Other", PipelineTemplateId = TemplateId, ExternalRef = "JOB-2" });
        foreach (var job in repo.Jobs)
            job.HiringManagers.Add(new JobHiringManager { JobId = job.Id, UserId = user.UserId!.Value });

        var result = await service.MoveStageAsync(otherJob, app.Id, Interview, app.RowVersion);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
        Assert.Equal(Applied, app.CurrentStageId);
        Assert.Empty(repo.Events);
        Assert.Empty(outbox.Staged);
    }

    // ---- Create: dedup, validation, atomicity -------------------------------------------------

    [Fact]
    public async Task Adding_a_candidate_already_on_the_job_does_not_duplicate_the_application()
    {
        var (service, repo, candidates, outbox) = Build();
        candidates.Candidates.Add(new Candidate { Id = 900, FirstName = "A", LastName = "B", Email = "a@b.test" });
        SeedApplication(repo);   // candidate 900 already applied to this job

        var result = await service.AddExistingCandidateToJobAsync(JobId, 900);

        Assert.True(result.Succeeded);
        Assert.Single(repo.Applications);
        Assert.Empty(outbox.Staged);
    }

    [Fact]
    public async Task A_job_whose_pipeline_has_no_stages_cannot_take_applications()
    {
        var (service, repo, candidates, _) = Build();
        repo.Stages.Clear();
        candidates.Candidates.Add(new Candidate { Id = 900, Email = "a@b.test" });

        var result = await service.AddExistingCandidateToJobAsync(JobId, 900);

        Assert.False(result.Succeeded);
        Assert.Contains("no stages", result.Error);
        Assert.Empty(repo.Applications);
    }

    [Fact]
    public async Task Adding_to_an_unknown_job_is_rejected()
    {
        var (service, _, candidates, _) = Build();
        candidates.Candidates.Add(new Candidate { Id = 900, Email = "a@b.test" });

        var result = await service.AddExistingCandidateToJobAsync(4040, 900);

        Assert.False(result.Succeeded);
        Assert.Contains("Job not found", result.Error);
    }

    [Fact]
    public async Task Adding_an_unknown_candidate_is_rejected()
    {
        var (service, _, _, _) = Build();

        var result = await service.AddExistingCandidateToJobAsync(JobId, 12345);

        Assert.False(result.Succeeded);
        Assert.Contains("Candidate not found", result.Error);
    }

    [Fact]
    public async Task A_new_application_starts_in_the_first_stage_as_active()
    {
        var (service, repo, candidates, outbox) = Build();
        candidates.Candidates.Add(new Candidate { Id = 900, Email = "a@b.test" });

        var result = await service.AddExistingCandidateToJobAsync(JobId, 900);

        Assert.True(result.Succeeded);
        var app = Assert.Single(repo.Applications);
        Assert.Equal(Applied, app.CurrentStageId);          // lowest Order
        Assert.Equal(ApplicationStatus.Active, app.Status);
        Assert.Equal(ApplicationOrigin.Manual, app.Origin);
        Assert.Single(repo.Events);
        Assert.Single(outbox.Staged);
    }

    [Fact]
    public async Task Creating_an_application_commits_as_one_transaction()
    {
        // DATA-6: the application, its first event and the outbox message must be all-or-nothing.
        var (service, repo, candidates, _) = Build();
        candidates.Candidates.Add(new Candidate { Id = 900, Email = "a@b.test" });

        await service.AddExistingCandidateToJobAsync(JobId, 900);

        Assert.Equal(1, repo.TransactionCount);
        Assert.Equal(repo.SaveCount, repo.SavesInsideTransaction);   // nothing saved outside it
    }

    [Fact]
    public async Task Adding_a_candidate_by_email_requires_an_email()
    {
        var (service, _, _, _) = Build();

        var result = await service.AddCandidateToJobAsync(
            new AddCandidateToJobInput(JobId, "A", "B", "   ", null));

        Assert.False(result.Succeeded);
        Assert.Contains("Email is required", result.Error);
    }

    [Fact]
    public async Task Adding_by_email_reuses_an_existing_candidate_rather_than_duplicating_them()
    {
        var (service, repo, candidates, _) = Build();
        candidates.Candidates.Add(new Candidate { Id = 900, FirstName = "A", LastName = "B", Email = "a@b.test" });

        var result = await service.AddCandidateToJobAsync(
            new AddCandidateToJobInput(JobId, "A", "B", "A@B.TEST", null));   // case-insensitive

        Assert.True(result.Succeeded);
        Assert.Single(candidates.Candidates);
        Assert.Equal(900, Assert.Single(repo.Applications).CandidateId);
    }

    // ---- Remove from job ---------------------------------------------------------------------

    [Fact]
    public async Task Removing_an_unknown_application_fails()
    {
        var (service, repo, _, _) = Build();

        var result = await service.RemoveAsync(12345);

        Assert.False(result.Succeeded);
        Assert.Equal("Application not found.", result.Error);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Removing_an_application_soft_deletes_it()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);

        var result = await service.RemoveAsync(app.Id);

        Assert.True(result.Succeeded);
        Assert.True(app.IsDeleted);
        Assert.Contains(app, repo.Applications);   // soft delete: the row stays
        Assert.Equal(1, repo.SaveCount);
    }

    [Fact]
    public async Task Removing_an_application_leaves_the_candidate_and_their_other_applications_alone()
    {
        var (service, repo, candidates, _) = Build();
        var candidate = new Candidate { Id = 900, FirstName = "A", LastName = "B", Email = "a@b.test" };
        candidates.Candidates.Add(candidate);
        var app = SeedApplication(repo);
        var other = new JobApplication { Id = 501, CandidateId = 900, JobId = 2, CurrentStageId = Applied, Status = ApplicationStatus.Active };
        repo.Applications.Add(other);

        await service.RemoveAsync(app.Id);

        Assert.False(candidate.IsDeleted);
        Assert.Equal(0, candidates.SaveCount);
        Assert.False(other.IsDeleted);
    }

    [Fact]
    public async Task Removing_an_application_during_a_concurrent_board_move_reports_a_friendly_failure()
    {
        var (service, repo, _, _) = Build();
        var app = SeedApplication(repo);
        repo.ConcurrencyConflict = true;

        var result = await service.RemoveAsync(app.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("This application was changed by someone else. Reload the board and try again.", result.Error);
    }
}
