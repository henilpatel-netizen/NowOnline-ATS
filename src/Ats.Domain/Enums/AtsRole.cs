namespace Ats.Domain.Enums;

public static class AtsRole
{
    public const string Owner = "Owner";
    public const string Recruiter = "Recruiter";
    public const string HiringManager = "HiringManager";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Owner, Recruiter, HiringManager, Viewer };

    // Roles an Owner may give a user from the Users screen. HiringManager has been assignable since
    // phase 3 scoped it to the jobs assigned to it.
    public static readonly string[] Assignable = { Owner, Recruiter, HiringManager, Viewer };
}
