using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Jobs;

// Job status transitions decide what the public career site exposes, so a wrong transition is
// externally visible. Previously untested (QUAL-1).
public class JobServiceTests
{
    private static (JobService Service, FakeJobRepository Repo) Build(params Job[] jobs)
    {
        var repo = new FakeJobRepository();
        repo.Jobs.AddRange(jobs);
        return (new JobService(repo), repo);
    }

    private static Job DraftJob(int id = 1) => new()
    {
        Id = id,
        Title = "Dev",
        PipelineTemplateId = 3,
        Status = JobStatus.Draft,
        ExternalRef = $"JOB-{id}"
    };

    private static JobInput Input(int? id = null, string title = "Developer", int pipelineId = 3) =>
        new(id, title, null, null, null, EmploymentType.FullTime, pipelineId);

    // ---- Create -------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_job_needs_a_title(string title)
    {
        var (service, repo) = Build();

        var result = await service.CreateAsync(Input(title: title));

        Assert.False(result.Succeeded);
        Assert.Contains("Title is required", result.Error);
        Assert.False(repo.AddCalled);
    }

    [Fact]
    public async Task A_job_needs_a_pipeline_that_exists()
    {
        var (service, repo) = Build();
        repo.PipelineExists = false;

        var result = await service.CreateAsync(Input());

        Assert.False(result.Succeeded);
        Assert.Contains("valid pipeline", result.Error);
        Assert.False(repo.AddCalled);
    }

    [Fact]
    public async Task A_new_job_starts_as_a_draft_with_a_numbered_reference()
    {
        var (service, repo) = Build();
        repo.NextNumber = 77;

        var result = await service.CreateAsync(Input(title: "  Developer  "));

        Assert.True(result.Succeeded);
        var job = Assert.Single(repo.Jobs);
        Assert.Equal("Developer", job.Title);                 // trimmed
        Assert.Equal(JobStatus.Draft, job.Status);            // never created published
        Assert.Equal("JOB-77", job.ExternalRef);
        Assert.Null(job.PublishedAt);
    }

    [Fact]
    public async Task A_clashing_job_number_is_reported_rather_than_throwing()
    {
        var (service, repo) = Build();
        repo.NumberClash = true;

        var result = await service.CreateAsync(Input());

        Assert.False(result.Succeeded);
        Assert.Contains("job number", result.Error);
    }

    // ---- Update -------------------------------------------------------------------------------

    [Fact]
    public async Task Updating_without_an_id_is_rejected()
    {
        var (service, _) = Build(DraftJob());

        var result = await service.UpdateAsync(Input(id: null));

        Assert.False(result.Succeeded);
        Assert.Contains("Missing job id", result.Error);
    }

    [Fact]
    public async Task Updating_an_unknown_job_is_rejected()
    {
        var (service, _) = Build();

        var result = await service.UpdateAsync(Input(id: 999));

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Updating_does_not_change_the_status_or_the_reference()
    {
        // An edit must not silently publish or unpublish a job, nor renumber it.
        var job = DraftJob();
        job.Status = JobStatus.Published;
        job.PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var (service, _) = Build(job);

        var result = await service.UpdateAsync(Input(id: job.Id, title: "Renamed"));

        Assert.True(result.Succeeded);
        Assert.Equal("Renamed", job.Title);
        Assert.Equal(JobStatus.Published, job.Status);
        Assert.Equal("JOB-1", job.ExternalRef);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), job.PublishedAt);
    }

    // ---- Publish / Close ----------------------------------------------------------------------

    [Fact]
    public async Task Publishing_a_draft_publishes_it_and_stamps_the_date()
    {
        var job = DraftJob();
        var (service, _) = Build(job);

        var result = await service.PublishAsync(job.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(JobStatus.Published, job.Status);
        Assert.NotNull(job.PublishedAt);
    }

    [Fact]
    public async Task Publishing_an_already_published_job_is_rejected()
    {
        var job = DraftJob();
        job.Status = JobStatus.Published;
        var (service, _) = Build(job);

        var result = await service.PublishAsync(job.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("already published", result.Error);
    }

    [Fact]
    public async Task Re_publishing_a_closed_job_keeps_the_original_published_date()
    {
        // PublishedAt uses ??=, so the first publication date is the one that sticks.
        var original = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);
        var job = DraftJob();
        job.Status = JobStatus.Closed;
        job.PublishedAt = original;
        var (service, _) = Build(job);

        await service.PublishAsync(job.Id);

        Assert.Equal(JobStatus.Published, job.Status);
        Assert.Equal(original, job.PublishedAt);
    }

    [Fact]
    public async Task Only_a_published_job_can_be_closed()
    {
        var job = DraftJob();   // still a draft
        var (service, _) = Build(job);

        var result = await service.CloseAsync(job.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("Only a published job", result.Error);
        Assert.Equal(JobStatus.Draft, job.Status);
    }

    [Fact]
    public async Task Closing_a_published_job_closes_it()
    {
        var job = DraftJob();
        job.Status = JobStatus.Published;
        var (service, _) = Build(job);

        var result = await service.CloseAsync(job.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(JobStatus.Closed, job.Status);
    }

    [Fact]
    public async Task Publishing_or_closing_an_unknown_job_is_rejected()
    {
        var (service, _) = Build();

        Assert.False((await service.PublishAsync(404)).Succeeded);
        Assert.False((await service.CloseAsync(404)).Succeeded);
    }

    // ---- Delete -------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_job_soft_deletes_it_rather_than_removing_the_row()
    {
        // Applications reference the job, so the row must survive; the global filter hides it.
        var job = DraftJob();
        var (service, repo) = Build(job);

        var result = await service.DeleteAsync(job.Id);

        Assert.True(result.Succeeded);
        Assert.True(job.IsDeleted);
        Assert.Single(repo.Jobs);
    }

    [Fact]
    public async Task Deleting_an_unknown_job_is_rejected()
    {
        var (service, _) = Build();

        var result = await service.DeleteAsync(404);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
    }
}
