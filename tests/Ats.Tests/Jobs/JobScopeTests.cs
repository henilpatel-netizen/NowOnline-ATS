using Ats.Application.Applications;
using Ats.Application.Candidates;
using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Jobs;

// A user with jobs.view but without jobs.viewall sees only the jobs they are assigned to, and the
// applications and candidates on them. Out of scope must look exactly like a missing id.
public class JobScopeTests
{
    private const int UserId = 7;
    private const int OwnJob = 1;
    private const int OtherJob = 2;
    private const int TemplateId = 10;
    private const int Applied = 100;
    private const int Interview = 101;
    private const int OwnApplication = 500;
    private const int OtherApplication = 501;
    private const int OwnCandidate = 900;
    private const int OtherCandidate = 901;

    private static FakeCurrentUser User(string? role) => new() { UserId = UserId, Role = role };

    private static FakeCurrentUser Anonymous() => new() { UserId = null, Role = null, IsAuthenticated = false };

    private static List<Job> Jobs()
    {
        var own = new Job { Id = OwnJob, Title = "Own", PipelineTemplateId = TemplateId, ExternalRef = "JOB-1" };
        own.HiringManagers.Add(new JobHiringManager { JobId = OwnJob, UserId = UserId });
        var other = new Job { Id = OtherJob, Title = "Other", PipelineTemplateId = TemplateId, ExternalRef = "JOB-2" };
        other.HiringManagers.Add(new JobHiringManager { JobId = OtherJob, UserId = UserId + 1 });
        return [own, other];
    }

    private static List<JobApplication> Applications() =>
    [
        new() { Id = OwnApplication, JobId = OwnJob, CandidateId = OwnCandidate, CurrentStageId = Applied, RowVersion = [1] },
        new() { Id = OtherApplication, JobId = OtherJob, CandidateId = OtherCandidate, CurrentStageId = Applied, RowVersion = [1] },
    ];

    private static JobService JobService(FakeCurrentUser user) => JobServiceWithRepo(user).Service;

    private static (JobService Service, FakeJobRepository Repo) JobServiceWithRepo(FakeCurrentUser user)
    {
        var repo = new FakeJobRepository();
        repo.Jobs.AddRange(Jobs());
        return (new JobService(repo, new FakeOutboxEnqueuer(), new JobScope(user)), repo);
    }

    private static JobInput Rename(int jobId) =>
        new(jobId, "Renamed", null, null, null, EmploymentType.FullTime, TemplateId, []);

    private static (ApplicationService Service, FakeApplicationRepository Repo) ApplicationService(FakeCurrentUser user)
    {
        var repo = new FakeApplicationRepository();
        repo.Jobs.AddRange(Jobs());
        repo.Applications.AddRange(Applications());
        repo.Stages.AddRange(
        [
            new PipelineStage { Id = Applied, PipelineTemplateId = TemplateId, Name = "Applied", Order = 1 },
            new PipelineStage { Id = Interview, PipelineTemplateId = TemplateId, Name = "Interview", Order = 2 },
        ]);
        var service = new ApplicationService(repo, new FakeCandidateRepository(), user, new FakeOutboxEnqueuer(), new JobScope(user));
        return (service, repo);
    }

    private static CandidateService CandidateService(FakeCurrentUser user)
    {
        var repo = new FakeCandidateRepository();
        repo.Jobs.AddRange(Jobs());
        repo.Applications.AddRange(Applications());
        repo.Candidates.AddRange(
        [
            new Candidate { Id = OwnCandidate, FirstName = "Own", LastName = "C", Email = "own@example.test" },
            new Candidate { Id = OtherCandidate, FirstName = "Other", LastName = "C", Email = "other@example.test" },
        ]);
        return new CandidateService(repo, new JobScope(user));
    }

    // ---- JobScope truth table ------------------------------------------------------------------

    [Theory]
    [InlineData(AtsRole.HiringManager, true)]
    [InlineData(AtsRole.Owner, false)]
    [InlineData(AtsRole.Recruiter, false)]
    [InlineData(AtsRole.Viewer, false)]
    [InlineData("Unknown", false)]
    public void Only_jobs_view_without_jobs_viewall_is_restricted(string role, bool restricted)
    {
        var scope = new JobScope(User(role));

        Assert.Equal(restricted, scope.IsRestricted);
        Assert.Equal(restricted ? UserId : null, scope.UserId);
    }

    [Fact]
    public void Anonymous_callers_are_unrestricted()
    {
        var scope = new JobScope(Anonymous());

        Assert.False(scope.IsRestricted);
        Assert.Null(scope.UserId);
    }

    [Fact]
    public void A_restricted_user_without_an_id_stays_restricted()
    {
        var scope = new JobScope(new FakeCurrentUser { UserId = null, Role = AtsRole.HiringManager });

        Assert.True(scope.IsRestricted);
        Assert.Null(scope.UserId);
    }

    // ---- Restricted user -----------------------------------------------------------------------

    [Fact]
    public async Task Restricted_user_sees_an_assigned_job_only()
    {
        var jobs = JobService(User(AtsRole.HiringManager));
        var (applications, _) = ApplicationService(User(AtsRole.HiringManager));

        Assert.NotNull(await jobs.GetAsync(OwnJob));
        Assert.Null(await jobs.GetAsync(OtherJob));
        Assert.NotNull(await applications.GetJobAsync(OwnJob));
        Assert.Null(await applications.GetJobAsync(OtherJob));
    }

    [Fact]
    public async Task Updating_an_unassigned_job_looks_like_a_missing_job()
    {
        var (service, repo) = JobServiceWithRepo(User(AtsRole.HiringManager));

        var outOfScope = await service.UpdateAsync(Rename(OtherJob));
        var missing = await service.UpdateAsync(Rename(404));

        Assert.False(outOfScope.Succeeded);
        Assert.Equal(missing.Error, outOfScope.Error);
        Assert.Equal("Other", repo.Jobs.Single(j => j.Id == OtherJob).Title);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Restricted_user_can_update_an_assigned_job()
    {
        var (service, repo) = JobServiceWithRepo(User(AtsRole.HiringManager));

        var result = await service.UpdateAsync(Rename(OwnJob));

        Assert.True(result.Succeeded);
        Assert.Equal("Renamed", repo.Jobs.Single(j => j.Id == OwnJob).Title);
    }

    [Fact]
    public async Task Restricted_user_sees_applications_on_assigned_jobs_only()
    {
        var (service, _) = ApplicationService(User(AtsRole.HiringManager));

        Assert.NotNull(await service.GetAsync(OwnApplication));
        Assert.Null(await service.GetAsync(OtherApplication));
        Assert.NotNull(await service.GetWithCandidateAsync(OwnApplication));
        Assert.Null(await service.GetWithCandidateAsync(OtherApplication));
    }

    [Fact]
    public async Task Restricted_user_sees_candidates_on_assigned_jobs_only()
    {
        var service = CandidateService(User(AtsRole.HiringManager));

        Assert.NotNull(await service.GetAsync(OwnCandidate));
        Assert.Null(await service.GetAsync(OtherCandidate));
    }

    [Fact]
    public async Task Out_of_scope_looks_the_same_as_a_missing_id()
    {
        var (service, _) = ApplicationService(User(AtsRole.HiringManager));

        var outOfScope = await service.MoveStageAsync(OtherJob, OtherApplication, Interview, [1]);
        var missing = await service.MoveStageAsync(OtherJob, 404, Interview, [1]);

        Assert.False(outOfScope.Succeeded);
        Assert.Equal(missing.Error, outOfScope.Error);
    }

    [Fact]
    public async Task Restricted_user_can_move_on_an_assigned_job()
    {
        var (service, repo) = ApplicationService(User(AtsRole.HiringManager));

        var result = await service.MoveStageAsync(OwnJob, OwnApplication, Interview, [1]);

        Assert.True(result.Succeeded);
        Assert.Equal(Interview, repo.Applications.Single(a => a.Id == OwnApplication).CurrentStageId);
    }

    [Fact]
    public async Task Restricted_user_cannot_move_on_an_unassigned_job()
    {
        var (service, repo) = ApplicationService(User(AtsRole.HiringManager));

        var result = await service.MoveStageAsync(OtherJob, OtherApplication, Interview, [1]);

        Assert.False(result.Succeeded);
        Assert.Equal(Applied, repo.Applications.Single(a => a.Id == OtherApplication).CurrentStageId);
        Assert.Empty(repo.Events);
    }

    [Fact]
    public async Task Restricted_user_gets_the_per_job_readers_for_an_assigned_job_only()
    {
        var (service, repo) = ApplicationService(User(AtsRole.HiringManager));
        var at = DateTimeOffset.UtcNow;
        repo.Events.Add(new ApplicationEvent { ApplicationId = OwnApplication, ToStageId = Applied, OccurredAt = at });
        repo.Events.Add(new ApplicationEvent { ApplicationId = OtherApplication, ToStageId = Applied, OccurredAt = at });

        Assert.NotEmpty(await service.GetStagesForJobAsync(OwnJob));
        Assert.NotEmpty(await service.ListForJobAsync(OwnJob));
        Assert.NotEmpty(await service.LatestEventTimesForJobAsync(OwnJob));
        Assert.NotEmpty(await service.ListEventsAsync(OwnApplication));

        Assert.Empty(await service.GetStagesForJobAsync(OtherJob));
        Assert.Empty(await service.ListForJobAsync(OtherJob));
        Assert.Empty(await service.LatestEventTimesForJobAsync(OtherJob));
        Assert.Empty(await service.ListEventsAsync(OtherApplication));
    }

    [Fact]
    public async Task Restricted_user_without_an_id_sees_nothing()
    {
        var user = new FakeCurrentUser { UserId = null, Role = AtsRole.HiringManager };
        var (applications, _) = ApplicationService(user);

        Assert.Null(await JobService(user).GetAsync(OwnJob));
        Assert.Null(await applications.GetJobAsync(OwnJob));
        Assert.Null(await applications.GetAsync(OwnApplication));
        Assert.Null(await applications.GetWithCandidateAsync(OwnApplication));
        Assert.False((await applications.MoveStageAsync(OwnJob, OwnApplication, Interview, [1])).Succeeded);
        Assert.Null(await CandidateService(user).GetAsync(OwnCandidate));
    }

    // ---- Unrestricted --------------------------------------------------------------------------

    [Theory]
    [InlineData(AtsRole.Owner)]
    [InlineData(AtsRole.Recruiter)]
    [InlineData(AtsRole.Viewer)]
    [InlineData(null)]
    public async Task Unrestricted_callers_see_every_job_application_and_candidate(string? role)
    {
        var user = role is null ? Anonymous() : User(role);
        var (applications, _) = ApplicationService(user);

        Assert.NotNull(await JobService(user).GetAsync(OtherJob));
        Assert.NotNull(await applications.GetJobAsync(OtherJob));
        Assert.NotNull(await applications.GetAsync(OtherApplication));
        Assert.NotNull(await applications.GetWithCandidateAsync(OtherApplication));
        Assert.NotEmpty(await applications.GetStagesForJobAsync(OtherJob));
        Assert.NotEmpty(await applications.ListForJobAsync(OtherJob));
        Assert.NotNull(await CandidateService(user).GetAsync(OtherCandidate));
    }

    [Theory]
    [InlineData(AtsRole.Owner)]
    [InlineData(AtsRole.Recruiter)]
    public async Task Unrestricted_callers_update_any_job_without_a_scope_query(string role)
    {
        var (service, repo) = JobServiceWithRepo(User(role));

        var result = await service.UpdateAsync(Rename(OtherJob));

        Assert.True(result.Succeeded);
        Assert.Equal(0, repo.AssignedQueries);
    }

    [Theory]
    [InlineData(AtsRole.Owner)]
    [InlineData(AtsRole.Recruiter)]
    [InlineData(AtsRole.Viewer)]
    [InlineData(null)]
    public async Task Unrestricted_callers_can_move_on_any_job(string? role)
    {
        var user = role is null ? Anonymous() : User(role);
        var (service, _) = ApplicationService(user);

        var result = await service.MoveStageAsync(OtherJob, OtherApplication, Interview, [1]);

        Assert.True(result.Succeeded);
    }
}
