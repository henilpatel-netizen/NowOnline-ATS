using Ats.Application.Career;
using Ats.Application.Integration;
using Ats.Application.Notifications;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Career;

public class CareerServiceTests
{
    private const int JobId = 1;
    private const int TemplateId = 10;

    private static (CareerService Service, FakeApplicationRepository Repo, FakeNotificationRecipientsQuery Recipients,
                    FakeEmailOutbox Emails) Build()
    {
        var job = new Job { Id = JobId, Title = "Developer", PipelineTemplateId = TemplateId, ExternalRef = "JOB-1", Status = JobStatus.Published };
        var repo = new FakeApplicationRepository();
        repo.Jobs.Add(job);
        repo.Stages.Add(new PipelineStage { Id = 100, PipelineTemplateId = TemplateId, Name = "Applied", Order = 1 });
        var recipients = new FakeNotificationRecipientsQuery();
        recipients.Add(1, AtsRole.Recruiter);
        recipients.Add(2, AtsRole.HiringManager);
        recipients.Assign(JobId, 2);
        var emails = new FakeEmailOutbox();
        repo.Saved += emails.Commit;
        var notifier = new HiringNotifier(recipients, emails, new FakeTenantBranding(), new FakeTenantContext(),
            new IntegrationOptions { CareerSiteBaseUrl = "https://ats.example" });
        var service = new CareerService(new FakeCareerRepository(job), new FakeCandidateRepository(), repo,
            new FakeOutboxEnqueuer(), notifier);
        return (service, repo, recipients, emails);
    }

    private static ApplyInput Input() =>
        new("JOB-1", "Ada", "Lovelace", "ada@private.test", "0612345678", null, "cv-key.pdf");

    [Fact]
    public async Task A_new_application_mails_each_recipient_once_in_the_same_unit_of_work()
    {
        var (service, repo, _, emails) = Build();

        var result = await service.ApplyAsync(Input());

        Assert.True(result.Succeeded);
        Assert.Equal(1, repo.TransactionCount);
        Assert.Equal(["user1@acme.test", "user2@acme.test"], emails.Committed.Select(c => c.Draft.To).OrderBy(t => t));
        Assert.All(emails.Committed, c =>
        {
            Assert.Contains("Ada Lovelace", c.Draft.TextBody);
            Assert.Contains("Developer", c.Draft.TextBody);
            Assert.Contains("https://ats.example/Board?jobId=1", c.Draft.TextBody);
            Assert.DoesNotContain("ada@private.test", c.Draft.TextBody + c.Draft.HtmlBody);
            Assert.DoesNotContain("0612345678", c.Draft.TextBody + c.Draft.HtmlBody);
            Assert.DoesNotContain("cv-key", c.Draft.TextBody + c.Draft.HtmlBody);
        });
    }

    [Fact]
    public async Task A_re_apply_to_the_same_job_mails_no_one()
    {
        var (service, repo, _, emails) = Build();
        await service.ApplyAsync(Input());
        emails.Staged.Clear();
        emails.Committed.Clear();

        var result = await service.ApplyAsync(Input());

        Assert.True(result.Succeeded);
        Assert.Single(repo.Applications);
        Assert.Empty(emails.Staged);
    }

    private sealed class FakeCareerRepository(Job job) : ICareerRepository
    {
        public Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default) => Task.FromResult(new List<Job> { job });

        public Task<Job?> GetPublishedJobByExternalRefAsync(string externalRef, CancellationToken ct = default) =>
            Task.FromResult(externalRef == job.ExternalRef ? job : null);

        public Task<string> GetCodeParameterNameAsync(CancellationToken ct = default) => Task.FromResult("ref");
    }
}
