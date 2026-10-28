using Ats.Application.Integration;
using Ats.Application.Notifications;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Notifications;

public class HiringNotifierTests
{
    private const int JobId = 5;
    private const int OtherJobId = 6;

    private static (HiringNotifier Notifier, FakeNotificationRecipientsQuery Query, FakeEmailOutbox Outbox) Build()
    {
        var query = new FakeNotificationRecipientsQuery();
        var outbox = new FakeEmailOutbox();
        var notifier = new HiringNotifier(query, outbox, new FakeTenantBranding(), new FakeTenantContext { CurrentTenantId = 3 },
            new IntegrationOptions { CareerSiteBaseUrl = "https://ats.example/" });
        return (notifier, query, outbox);
    }

    private static List<string> To(FakeEmailOutbox outbox) => outbox.Staged.Select(s => s.Draft.To).OrderBy(t => t).ToList();

    [Fact]
    public async Task A_new_application_mails_every_recruiter_and_the_assigned_hiring_managers_once_each()
    {
        var (notifier, query, outbox) = Build();
        query.Add(1, AtsRole.Recruiter);
        query.Add(2, AtsRole.Recruiter);
        query.Add(3, AtsRole.HiringManager);
        query.Add(4, AtsRole.HiringManager);   // assigned to another job only
        query.Add(5, AtsRole.Owner);           // a Recruiter exists, so no Owner
        query.Add(6, AtsRole.Viewer);
        query.Assign(JobId, 3);
        query.Assign(JobId, 3);                // duplicate link
        query.Assign(OtherJobId, 4);

        await notifier.StageNewApplicationAsync(JobId, "Developer", "Ada Lovelace");

        Assert.Equal(["user1@acme.test", "user2@acme.test", "user3@acme.test"], To(outbox));
        Assert.All(outbox.Staged, s => Assert.Equal(3, s.TenantId));
    }

    [Fact]
    public async Task Without_an_active_accepted_recruiter_the_owners_get_new_applications()
    {
        var (notifier, query, outbox) = Build();
        query.Add(1, AtsRole.Recruiter, active: false);
        query.Add(2, AtsRole.Recruiter, accepted: false);
        query.Add(3, AtsRole.Owner);
        query.Add(4, AtsRole.Owner);

        await notifier.StageNewApplicationAsync(JobId, "Developer", "Ada Lovelace");

        Assert.Equal(["user3@acme.test", "user4@acme.test"], To(outbox));
    }

    [Fact]
    public async Task Inactive_and_invite_pending_users_are_never_mailed()
    {
        var (notifier, query, outbox) = Build();
        query.Add(1, AtsRole.Recruiter);
        query.Add(2, AtsRole.Recruiter, active: false);
        query.Add(3, AtsRole.Recruiter, accepted: false);
        query.Add(4, AtsRole.HiringManager, active: false);
        query.Add(5, AtsRole.HiringManager, accepted: false);
        query.Add(6, AtsRole.Owner, active: false);
        query.Assign(JobId, 4);
        query.Assign(JobId, 5);

        await notifier.StageNewApplicationAsync(JobId, "Developer", "Ada Lovelace");
        await notifier.StageStageChangedAsync(JobId, "Developer", "Ada Lovelace", "Interview", movedByUserId: null);

        Assert.Equal(["user1@acme.test"], To(outbox));
    }

    [Fact]
    public async Task A_new_application_email_links_to_the_job_board_and_carries_no_candidate_contact_data()
    {
        var (notifier, query, outbox) = Build();
        query.Add(1, AtsRole.Recruiter);

        await notifier.StageNewApplicationAsync(JobId, "Developer", "Ada Lovelace");

        var draft = Assert.Single(outbox.Staged).Draft;
        Assert.Contains("https://ats.example/Board?jobId=5", draft.TextBody);
        Assert.Contains("Ada Lovelace", draft.TextBody);
        Assert.Contains("Developer", draft.TextBody);
    }

    [Fact]
    public async Task A_stage_change_mails_the_assigned_hiring_managers_except_the_mover()
    {
        var (notifier, query, outbox) = Build();
        query.Add(1, AtsRole.Recruiter);
        query.Add(2, AtsRole.Owner);
        query.Add(3, AtsRole.HiringManager);
        query.Add(4, AtsRole.HiringManager);
        query.Add(5, AtsRole.HiringManager);   // not on this job
        query.Assign(JobId, 3);
        query.Assign(JobId, 4);
        query.Assign(OtherJobId, 5);

        await notifier.StageStageChangedAsync(JobId, "Developer", "Ada Lovelace", "Interview", movedByUserId: 4);

        var draft = Assert.Single(outbox.Staged).Draft;
        Assert.Equal("user3@acme.test", draft.To);
        Assert.Contains("Interview", draft.TextBody);
        Assert.Contains("https://ats.example/Board?jobId=5", draft.TextBody);
    }

    [Fact]
    public async Task A_stage_change_on_a_job_without_hiring_managers_mails_no_one()
    {
        var (notifier, query, outbox) = Build();
        query.Add(1, AtsRole.Recruiter);
        query.Add(2, AtsRole.Owner);
        query.Add(3, AtsRole.HiringManager);

        await notifier.StageStageChangedAsync(JobId, "Developer", "Ada Lovelace", "Interview", movedByUserId: 1);

        Assert.Empty(outbox.Staged);
    }

    [Fact]
    public async Task Duplicate_recipients_get_one_message()
    {
        var outbox = new FakeEmailOutbox();
        var notifier = new HiringNotifier(new DuplicatingQuery(), outbox, new FakeTenantBranding(), new FakeTenantContext(),
            new IntegrationOptions { CareerSiteBaseUrl = "https://ats.example" });

        await notifier.StageNewApplicationAsync(JobId, "Developer", "Ada Lovelace");
        await notifier.StageStageChangedAsync(JobId, "Developer", "Ada Lovelace", "Interview", movedByUserId: null);

        Assert.Equal(2, outbox.Staged.Count);
    }

    private sealed class DuplicatingQuery : INotificationRecipientsQuery
    {
        private static readonly IReadOnlyList<Recipient> Same = [new(1, "a@acme.test"), new(1, "a@acme.test")];
        public Task<IReadOnlyList<Recipient>> NewApplicationAsync(int jobId, CancellationToken ct = default) => Task.FromResult(Same);
        public Task<IReadOnlyList<Recipient>> StageChangeAsync(int jobId, CancellationToken ct = default) => Task.FromResult(Same);
    }
}
