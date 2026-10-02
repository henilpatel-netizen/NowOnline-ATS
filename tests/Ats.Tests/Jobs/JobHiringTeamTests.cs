using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Jobs;

// The hiring team decides which jobs a HiringManager can see, so a wrongly accepted id would hand a
// user (possibly another tenant's) access to a job. Every id must be an active HiringManager that the
// tenant-filtered context returns; anything else rejects the whole save with one generic message.
public class JobHiringTeamTests
{
    private const string Rejected = "One or more of the selected hiring managers cannot be assigned. Reload the page and choose again.";

    private static (JobService Service, FakeJobRepository Repo) Build(params Job[] jobs)
    {
        var repo = new FakeJobRepository();
        repo.Jobs.AddRange(jobs);
        repo.Users[7] = new FakeTeamUser("Sanne de Vries");
        repo.Users[8] = new FakeTeamUser("Jan Jansen");
        repo.Users[9] = new FakeTeamUser("Aylin Demir");
        repo.Users[20] = new FakeTeamUser("Ex Manager", IsActive: false);
        repo.Users[21] = new FakeTeamUser("Rita Recruiter", Role: AtsRole.Recruiter);
        return (new JobService(repo, new FakeOutboxEnqueuer(), new JobScope(new FakeCurrentUser())), repo);
    }

    private static Job JobWithTeam(params int[] userIds)
    {
        var job = new Job { Id = 1, Title = "Dev", PipelineTemplateId = 3, ExternalRef = "JOB-1" };
        foreach (var id in userIds) job.HiringManagers.Add(new JobHiringManager { UserId = id });
        return job;
    }

    private static JobInput Input(int? id, params int[] team) =>
        new(id, "Developer", null, null, null, EmploymentType.FullTime, 3, team);

    private static int[] Team(Job job) => job.HiringManagers.Select(h => h.UserId).Order().ToArray();

    [Fact]
    public async Task A_new_job_is_created_with_the_selected_hiring_managers()
    {
        var (service, repo) = Build();

        var result = await service.CreateAsync(Input(null, 8, 7));

        Assert.True(result.Succeeded);
        Assert.Equal([7, 8], Team(Assert.Single(repo.Jobs)));
        Assert.Equal(["Jan Jansen", "Sanne de Vries"], result.Team.Added);
        Assert.Empty(result.Team.Removed);
    }

    [Fact]
    public async Task A_user_from_another_tenant_rejects_the_create_and_adds_no_job()
    {
        var (service, repo) = Build();

        // 555 exists only in another tenant, so the filtered context does not return it.
        var result = await service.CreateAsync(Input(null, 7, 555));

        Assert.False(result.Succeeded);
        Assert.Equal(Rejected, result.Error);
        Assert.False(repo.AddCalled);
        Assert.Empty(repo.Jobs);
        Assert.Equal(0, repo.SaveCount);
    }

    [Theory]
    [InlineData(555)]   // other tenant or missing
    [InlineData(20)]    // deactivated
    [InlineData(21)]    // not a hiring manager
    public async Task An_unassignable_user_rejects_the_whole_update_with_one_message(int badId)
    {
        var job = JobWithTeam(7);
        var (service, repo) = Build(job);

        var result = await service.UpdateAsync(Input(1, 8, badId));

        Assert.False(result.Succeeded);
        Assert.Equal(Rejected, result.Error);
        Assert.Equal([7], Team(job));
        Assert.Equal("Dev", job.Title);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Duplicate_ids_are_ignored_and_checked_in_one_query()
    {
        var (service, repo) = Build(JobWithTeam());

        var result = await service.UpdateAsync(Input(1, 7, 7, 8, 8));

        Assert.True(result.Succeeded);
        Assert.Equal([7, 8], Team(repo.Jobs[0]));
        Assert.Equal(1, repo.AssignableQueries);
    }

    [Fact]
    public async Task Updating_replaces_the_team_and_names_who_was_added_and_removed()
    {
        // 20 has been deactivated since being assigned; leaving them out removes them, by name.
        var job = JobWithTeam(7, 8, 20);
        var (service, _) = Build(job);

        var result = await service.UpdateAsync(Input(1, 8, 9));

        Assert.True(result.Succeeded);
        Assert.Equal([8, 9], Team(job));
        Assert.Equal(["Aylin Demir"], result.Team.Added);
        Assert.Equal(["Ex Manager", "Sanne de Vries"], result.Team.Removed);
    }

    [Fact]
    public async Task An_empty_team_is_allowed_and_clears_the_job()
    {
        var job = JobWithTeam(7);
        var (service, repo) = Build(job);

        var result = await service.UpdateAsync(Input(1));

        Assert.True(result.Succeeded);
        Assert.Empty(job.HiringManagers);
        Assert.Equal(["Sanne de Vries"], result.Team.Removed);
        Assert.Equal(0, repo.AssignableQueries);
    }

    [Fact]
    public async Task An_unchanged_team_reports_no_change()
    {
        var job = JobWithTeam(7, 8);
        var (service, _) = Build(job);

        var result = await service.UpdateAsync(Input(1, 8, 7));

        Assert.True(result.Succeeded);
        Assert.True(result.Team.IsEmpty);
        Assert.Equal([7, 8], Team(job));
    }

    [Fact]
    public async Task A_concurrent_duplicate_team_link_gives_a_friendly_retry_message()
    {
        var (service, repo) = Build(JobWithTeam());
        repo.RejectNextSaveAsDuplicateTeamLink = true;

        var result = await service.UpdateAsync(Input(1, 7));

        Assert.False(result.Succeeded);
        Assert.Equal("The job could not be saved just now. Reload the page and try again.", result.Error);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public void The_audit_text_lists_names_for_each_kind_of_change()
    {
        Assert.Equal("hiring team: added Aylin Demir, Jan Jansen; removed Sanne de Vries",
            new HiringTeamChange(["Aylin Demir", "Jan Jansen"], ["Sanne de Vries"]).Describe());
        Assert.Equal("hiring team: added Aylin Demir", new HiringTeamChange(["Aylin Demir"], []).Describe());
        Assert.Equal("hiring team: removed Jan Jansen", new HiringTeamChange([], ["Jan Jansen"]).Describe());
        Assert.Equal("", HiringTeamChange.None.Describe());
    }
}
