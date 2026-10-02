using System.Diagnostics.CodeAnalysis;

namespace Ats.Domain.Authorization;

// Policy names. Controllers and views check these, never role names.
[SuppressMessage("Naming", "CA1711", Justification = "A catalogue of permission names, not a CAS Permission type.")]
public static class AtsPermission
{
    public const string DashboardView = "dashboard.view";
    public const string JobsView = "jobs.view";
    public const string JobsManage = "jobs.manage";
    public const string CandidatesView = "candidates.view";
    public const string CandidatesManage = "candidates.manage";
    public const string ApplicationsMove = "applications.move";
    public const string ResumesDownload = "resumes.download";
    public const string PipelinesManage = "pipelines.manage";
    public const string OrganisationManage = "organisation.manage";
    public const string CareerSiteManage = "careersite.manage";
    public const string IntegrationManage = "integration.manage";
    public const string AuditView = "audit.view";
    public const string UsersManage = "users.manage";
    public const string ProfileManage = "profile.manage";

    public static readonly string[] All =
    {
        DashboardView, JobsView, JobsManage, CandidatesView, CandidatesManage, ApplicationsMove,
        ResumesDownload, PipelinesManage, OrganisationManage, CareerSiteManage, IntegrationManage,
        AuditView, UsersManage, ProfileManage,
    };
}
