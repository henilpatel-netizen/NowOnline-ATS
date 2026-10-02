namespace Ats.Application.Jobs;

// Which jobs the signed-in user may see. Restricted users see only the jobs they are assigned to
// as a hiring manager, and the applications and candidates on those jobs.
public interface IJobScope
{
    bool IsRestricted { get; }

    // The user the scope is limited to; null when unrestricted. Null while restricted means the
    // user cannot be identified, and callers must then show nothing.
    int? UserId { get; }
}
