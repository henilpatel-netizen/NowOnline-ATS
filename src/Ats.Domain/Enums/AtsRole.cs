namespace Ats.Domain.Enums;

public static class AtsRole
{
    public const string Owner = "Owner";
    public const string Recruiter = "Recruiter";
    public const string HiringManager = "HiringManager";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Owner, Recruiter, HiringManager, Viewer };
}
