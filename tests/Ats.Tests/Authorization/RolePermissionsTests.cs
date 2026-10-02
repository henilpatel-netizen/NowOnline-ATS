using Ats.Domain.Authorization;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Authorization;

// The matrix is a security boundary: an accidental grant here opens an endpoint to a role. Each role's
// expected set is spelled out in full so any added or removed permission fails a test.
public class RolePermissionsTests
{
    private static readonly string[] ReadOnly =
        { AtsPermission.DashboardView, AtsPermission.JobsView, AtsPermission.CandidatesView, AtsPermission.ProfileManage };

    public static TheoryData<string, string[]> Expected => new()
    {
        { AtsRole.Owner, AtsPermission.All },
        { AtsRole.Recruiter, [.. ReadOnly, AtsPermission.JobsViewAll, AtsPermission.JobsManage, AtsPermission.CandidatesManage,
            AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload,
            AtsPermission.PipelinesManage, AtsPermission.OrganisationManage] },
        { AtsRole.HiringManager, [.. ReadOnly, AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload] },
        { AtsRole.Viewer, [.. ReadOnly, AtsPermission.JobsViewAll] },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Role_has_exactly_its_permissions(string role, string[] expected)
    {
        var actual = AtsPermission.All.Where(p => RolePermissions.Has(role, p)).Order();
        Assert.Equal(expected.Order(), actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("owner")]      // role names are case-sensitive; the claim is written from AtsRole
    [InlineData("SuperAdmin")]
    public void Unknown_role_has_nothing(string? role) =>
        Assert.DoesNotContain(AtsPermission.All, p => RolePermissions.Has(role, p));

    [Fact]
    public void Admin_only_permissions_resolve_to_owner_only()
    {
        foreach (var p in new[] { AtsPermission.IntegrationManage, AtsPermission.AuditView,
                     AtsPermission.UsersManage, AtsPermission.CareerSiteManage })
            Assert.Equal(new[] { AtsRole.Owner }, RolePermissions.RolesWith(p));
    }

    [Fact]
    public void Every_permission_is_granted_to_some_role() =>
        Assert.All(AtsPermission.All, p => Assert.NotEmpty(RolePermissions.RolesWith(p)));

    [Fact]
    public void Every_role_can_manage_its_own_profile() =>
        Assert.All(AtsRole.All, r => Assert.True(RolePermissions.Has(r, AtsPermission.ProfileManage), r));

    [Fact]
    public void Assignable_is_Owner_Recruiter_HiringManager_Viewer() =>
        Assert.Equal(new[] { AtsRole.Owner, AtsRole.Recruiter, AtsRole.HiringManager, AtsRole.Viewer }, AtsRole.Assignable);

    [Fact]
    public void Only_HiringManager_views_jobs_without_viewing_all() =>
        Assert.Equal(new[] { AtsRole.HiringManager },
            AtsRole.All.Where(r => RolePermissions.Has(r, AtsPermission.JobsView) && !RolePermissions.Has(r, AtsPermission.JobsViewAll)));

    // Manage paths (the Jobs Edit POST team reload, the add-candidate job pickers,
    // AddExistingCandidateToJob) skip the job-scope check, so they must never serve a job-scoped user.
    // ApplicationsMove is left out: HiringManager holds it and is scoped by design.
    [Fact]
    public void Every_role_that_manages_jobs_or_candidates_views_all_jobs() =>
        Assert.All(AtsRole.All.Where(r => RolePermissions.Has(r, AtsPermission.JobsManage) || RolePermissions.Has(r, AtsPermission.CandidatesManage)),
            r => Assert.True(RolePermissions.Has(r, AtsPermission.JobsViewAll), r));

    [Fact]
    public void Permission_names_are_unique() =>
        Assert.Equal(AtsPermission.All.Length, AtsPermission.All.Distinct().Count());
}
