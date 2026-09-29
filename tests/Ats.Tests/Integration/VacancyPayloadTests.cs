using System.Text.Json;
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Integration;

// The snapshot is what ReferralTool stores and shows referrers; its limits are ReferralTool's model limits.
public class VacancyPayloadTests
{
    private static Job PublishedJob() => new()
    {
        Id = 5,
        Title = "Senior .NET Developer",
        ExternalRef = "JOB-5",
        Status = JobStatus.Published,
        EmploymentType = EmploymentType.PartTime
    };

    [Fact]
    public void Maps_the_job_to_the_referraltool_shape()
    {
        var p = VacancyPayload.From(PublishedJob(), "https://careers.example.com/", "acme", "Amsterdam", "Engineering");

        Assert.Equal("JOB-5", p.Id);
        Assert.Equal("Senior .NET Developer", p.Title);
        Assert.Equal("https://careers.example.com/careers/acme/jobs/JOB-5", p.Url);
        Assert.Equal("Amsterdam", p.Location);
        Assert.Equal("PartTime", p.EmploymentType);
        Assert.Equal(new[] { "Engineering" }, p.Categories);
        Assert.False(p.Inactive);
    }

    [Fact]
    public void A_title_longer_than_referraltool_allows_is_cut_to_150()
    {
        var job = PublishedJob();
        job.Title = new string('x', 200);

        Assert.Equal(150, VacancyPayload.From(job, "https://c.example", "acme", null, null).Title.Length);
    }

    [Fact]
    public void No_department_means_no_categories() =>
        Assert.Empty(VacancyPayload.From(PublishedJob(), "https://c.example", "acme", null, null).Categories);

    [Theory]
    [InlineData(JobStatus.Closed, false)]
    [InlineData(JobStatus.Draft, false)]
    [InlineData(JobStatus.Published, true)]
    public void Only_a_live_published_job_is_active(JobStatus status, bool deleted)
    {
        var job = PublishedJob();
        job.Status = status;
        job.IsDeleted = deleted;

        Assert.True(VacancyPayload.From(job, "https://c.example", "acme", null, null).Inactive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{not json")]
    [InlineData("{}")]
    public void An_unreadable_snapshot_parses_to_null(string? json) =>
        Assert.Null(VacancyPayload.TryParse(json));

    [Fact]
    public void A_snapshot_round_trips()
    {
        var p = VacancyPayload.From(PublishedJob(), "https://c.example", "acme", "Amsterdam", "Engineering");
        Assert.Equal(p.Url, VacancyPayload.TryParse(JsonSerializer.Serialize(p))!.Url);
        Assert.Equal(p.Categories, VacancyPayload.TryParse(JsonSerializer.Serialize(p))!.Categories);
    }
}
