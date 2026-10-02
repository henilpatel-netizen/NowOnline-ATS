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
        { AtsRole.Recruiter, [.. ReadOnly, AtsPermission.JobsManage, AtsPermission.CandidatesManage,
            AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload,
            AtsPermission.PipelinesManage, AtsPermission.OrganisationManage] },
        { AtsRole.HiringManager, [.. ReadOnly, AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload] },
        { AtsRole.Viewer, ReadOnly },
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
    public void HiringManager_is_not_assignable_until_it_is_scoped()
    {
        Assert.DoesNotContain(AtsRole.HiringManager, AtsRole.Assignable);
        Assert.Equal(new[] { AtsRole.Owner, AtsRole.Recruiter, AtsRole.Viewer }, AtsRole.Assignable);
    }

    [Fact]
    public void Permission_names_are_unique() =>
        Assert.Equal(AtsPermission.All.Length, AtsPermission.All.Distinct().Count());
}
