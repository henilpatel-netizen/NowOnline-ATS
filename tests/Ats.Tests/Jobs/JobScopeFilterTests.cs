using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Xunit;

namespace Ats.Tests.Jobs;

// The shared predicate behind every scoped list and count, run over in-memory data. Soft delete is
// the jobs source's job (EF's filter): here a job missing from that source stands in for it.
public class JobScopeFilterTests
{
    private const int UserId = 7;
    private const int OwnJob = 1;
    private const int OtherJob = 2;
    private const int OwnCandidate = 900;
    private const int OtherCandidate = 901;
    private const int SharedCandidate = 902;

    private sealed class Scope(bool restricted, int? userId) : IJobScope
    {
        public bool IsRestricted => restricted;
        public int? UserId => userId;
    }

    private static readonly IJobScope Unrestricted = new Scope(false, null);
    private static readonly IJobScope Restricted = new Scope(true, UserId);
    private static readonly IJobScope RestrictedWithoutId = new Scope(true, null);

    private static List<Job> Jobs()
    {
        var own = new Job { Id = OwnJob };
        own.HiringManagers.Add(new JobHiringManager { JobId = OwnJob, UserId = UserId });
        var other = new Job { Id = OtherJob };
        other.HiringManagers.Add(new JobHiringManager { JobId = OtherJob, UserId = UserId + 1 });
        return [own, other];
    }

    private static readonly JobApplication[] Applications =
    [
        new() { Id = 10, JobId = OwnJob, CandidateId = OwnCandidate },
        new() { Id = 11, JobId = OtherJob, CandidateId = OtherCandidate },
        new() { Id = 12, JobId = OtherJob, CandidateId = SharedCandidate },
        new() { Id = 13, JobId = OwnJob, CandidateId = SharedCandidate },
    ];

    private static readonly Candidate[] Candidates =
    [
        new() { Id = OwnCandidate }, new() { Id = OtherCandidate }, new() { Id = SharedCandidate },
    ];

    private static readonly ApplicationEvent[] Events =
    [
        new() { Id = 20, ApplicationId = 10 }, new() { Id = 21, ApplicationId = 11 },
        new() { Id = 22, ApplicationId = 12 }, new() { Id = 23, ApplicationId = 13 },
    ];

    private static int[] JobIds(IJobScope scope, List<Job>? jobs = null) =>
        (jobs ?? Jobs()).AsQueryable().VisibleTo(scope).Select(j => j.Id).ToArray();

    private static int[] ApplicationIds(IJobScope scope, List<Job>? jobs = null) =>
        Applications.AsQueryable().VisibleTo((jobs ?? Jobs()).AsQueryable(), scope).Select(a => a.Id).ToArray();

    private static int[] CandidateIds(IJobScope scope, List<Job>? jobs = null) =>
        Candidates.AsQueryable().VisibleTo(Applications.AsQueryable(), (jobs ?? Jobs()).AsQueryable(), scope)
            .Select(c => c.Id).ToArray();

    private static int[] EventIds(IJobScope scope, List<Job>? jobs = null) =>
        Events.AsQueryable().VisibleTo(Applications.AsQueryable(), (jobs ?? Jobs()).AsQueryable(), scope)
            .Select(e => e.Id).ToArray();

    [Fact]
    public void Unrestricted_leaves_every_query_unchanged()
    {
        Assert.Equal([OwnJob, OtherJob], JobIds(Unrestricted));
        Assert.Equal([10, 11, 12, 13], ApplicationIds(Unrestricted));
        Assert.Equal([OwnCandidate, OtherCandidate, SharedCandidate], CandidateIds(Unrestricted));
        Assert.Equal([20, 21, 22, 23], EventIds(Unrestricted));
    }

    [Fact]
    public void Unrestricted_applications_do_not_depend_on_the_jobs_source() =>
        Assert.Equal([10, 11, 12, 13], ApplicationIds(Unrestricted, []));

    [Fact]
    public void Restricted_sees_own_jobs_only() =>
        Assert.Equal([OwnJob], JobIds(Restricted));

    [Fact]
    public void Restricted_sees_applications_on_own_jobs_only() =>
        Assert.Equal([10, 13], ApplicationIds(Restricted));

    [Fact]
    public void Restricted_sees_candidates_with_an_application_on_an_own_job() =>
        Assert.Equal([OwnCandidate, SharedCandidate], CandidateIds(Restricted));

    [Fact]
    public void Restricted_sees_events_of_applications_on_own_jobs_only() =>
        Assert.Equal([20, 23], EventIds(Restricted));

    [Fact]
    public void Restricted_without_an_id_sees_nothing()
    {
        Assert.Empty(JobIds(RestrictedWithoutId));
        Assert.Empty(ApplicationIds(RestrictedWithoutId));
        Assert.Empty(CandidateIds(RestrictedWithoutId));
        Assert.Empty(EventIds(RestrictedWithoutId));
    }

    [Fact]
    public void An_own_job_the_jobs_source_hides_takes_its_applications_and_candidates_with_it()
    {
        var withoutOwnJob = Jobs().Where(j => j.Id != OwnJob).ToList();

        Assert.Empty(JobIds(Restricted, withoutOwnJob));
        Assert.Empty(ApplicationIds(Restricted, withoutOwnJob));
        Assert.Empty(CandidateIds(Restricted, withoutOwnJob));
        Assert.Empty(EventIds(Restricted, withoutOwnJob));
    }

    [Fact]
    public async Task AllowsAsync_skips_the_check_when_unrestricted()
    {
        var called = false;

        Assert.True(await Unrestricted.AllowsAsync(_ => { called = true; return Task.FromResult(false); }));
        Assert.False(called);
    }

    [Fact]
    public async Task AllowsAsync_asks_for_the_restricted_users_id()
    {
        int? asked = null;

        Assert.True(await Restricted.AllowsAsync(id => { asked = id; return Task.FromResult(true); }));
        Assert.Equal(UserId, asked);
        Assert.False(await Restricted.AllowsAsync(_ => Task.FromResult(false)));
    }

    [Fact]
    public async Task AllowsAsync_refuses_a_restricted_user_without_an_id() =>
        Assert.False(await RestrictedWithoutId.AllowsAsync(_ => Task.FromResult(true)));
}
