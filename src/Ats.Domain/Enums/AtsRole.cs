namespace Ats.Domain.Enums;

public static class AtsRole
{
    public const string Owner = "Owner";
    public const string Recruiter = "Recruiter";
    public const string HiringManager = "HiringManager";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Owner, Recruiter, HiringManager, Viewer };

    // Roles an Owner may give a user from the Users screen. HiringManager is withheld until phase 3
    // limits it to its own jobs; until then it would see every job and candidate in the tenant.
    public static readonly string[] Assignable = { Owner, Recruiter, Viewer };
}
