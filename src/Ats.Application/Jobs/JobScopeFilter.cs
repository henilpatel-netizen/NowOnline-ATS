using Ats.Domain.Entities;

namespace Ats.Application.Jobs;

// The one job-scope predicate every read model applies. It is plain LINQ (no EF), so it is unit-tested
// over in-memory data, like UserListFilter. Applications and candidates are scoped by joining through
// the jobs queryable passed in: pass the context's Jobs set, so soft-deleted jobs drop out by its
// filter and visibility is never decided from JobHiringManagers alone.
public static class JobScopeFilter
{
    public static IQueryable<Job> AssignedTo(this IQueryable<Job> jobs, int userId) =>
        jobs.Where(j => j.HiringManagers.Any(h => h.UserId == userId));

    public static IQueryable<Job> VisibleTo(this IQueryable<Job> jobs, IJobScope scope)
    {
        if (!scope.IsRestricted) return jobs;
        return scope.UserId is int userId ? jobs.AssignedTo(userId) : jobs.Where(_ => false);
    }

    public static IQueryable<JobApplication> VisibleTo(this IQueryable<JobApplication> applications, IQueryable<Job> jobs, IJobScope scope)
    {
        if (!scope.IsRestricted) return applications;
        var visible = jobs.VisibleTo(scope);
        return applications.Where(a => visible.Any(j => j.Id == a.JobId));
    }

    public static IQueryable<Candidate> VisibleTo(this IQueryable<Candidate> candidates, IQueryable<JobApplication> applications,
        IQueryable<Job> jobs, IJobScope scope)
    {
        if (!scope.IsRestricted) return candidates;
        var visible = applications.VisibleTo(jobs, scope);
        return candidates.Where(c => visible.Any(a => a.CandidateId == c.Id));
    }

    public static IQueryable<ApplicationEvent> VisibleTo(this IQueryable<ApplicationEvent> events, IQueryable<JobApplication> applications,
        IQueryable<Job> jobs, IJobScope scope)
    {
        if (!scope.IsRestricted) return events;
        var visible = applications.VisibleTo(jobs, scope);
        return events.Where(e => visible.Any(a => a.Id == e.ApplicationId));
    }

    // The by-id form of the same rule: unrestricted callers always pass, a restricted user without an
    // id never does, otherwise isOwn decides for the user's id.
    public static async Task<bool> AllowsAsync(this IJobScope scope, Func<int, Task<bool>> isOwn) =>
        !scope.IsRestricted || (scope.UserId is int userId && await isOwn(userId));
}
