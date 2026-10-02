using Ats.Application.Abstractions;
using Ats.Domain.Authorization;

namespace Ats.Application.Jobs;

// Decided by permission: jobs.view without jobs.viewall. Anonymous callers (career site, worker)
// are unrestricted; they never reach the scoped paths, which all require a signed-in user.
public sealed class JobScope : IJobScope
{
    private readonly ICurrentUser _user;
    public JobScope(ICurrentUser user) => _user = user;

    public bool IsRestricted =>
        _user.IsAuthenticated
        && RolePermissions.Has(_user.Role, AtsPermission.JobsView)
        && !RolePermissions.Has(_user.Role, AtsPermission.JobsViewAll);

    public int? UserId => IsRestricted ? _user.UserId : null;
}
