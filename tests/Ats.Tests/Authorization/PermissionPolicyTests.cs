using System.Security.Claims;
using Ats.Domain.Authorization;
using Ats.Domain.Enums;
using Ats.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ats.Tests.Authorization;

// Proves the registered ASP.NET Core policies agree with RolePermissions for every role, i.e. that the
// map is actually what the framework enforces. This is the only per-role check until phase 2 lets the
// e2e suite sign in as non-Owner users.
public class PermissionPolicyTests
{
    private static readonly IAuthorizationService Auth = Build();

    private static IAuthorizationService Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(o => o.AddAtsPermissionPolicies());
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal SignedInAs(string role) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test"));

    [Fact]
    public async Task Every_role_and_permission_matches_the_map()
    {
        foreach (var role in AtsRole.All)
            foreach (var permission in AtsPermission.All)
            {
                var result = await Auth.AuthorizeAsync(SignedInAs(role), permission);
                Assert.True(result.Succeeded == RolePermissions.Has(role, permission), $"{role} / {permission}");
            }
    }

    [Fact]
    public async Task Anonymous_user_fails_every_permission()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        foreach (var permission in AtsPermission.All)
            Assert.False((await Auth.AuthorizeAsync(anonymous, permission)).Succeeded, permission);
    }

    [Fact]
    public void Fallback_policy_requires_an_authenticated_user()
    {
        var options = new AuthorizationOptions();
        options.AddAtsPermissionPolicies();
        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy!.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData(AtsRole.Viewer, AtsPermission.ResumesDownload, false)]
    [InlineData(AtsRole.Recruiter, AtsPermission.ResumesDownload, true)]
    public void Can_extension_reads_the_role_claim(string role, string permission, bool expected) =>
        Assert.Equal(expected, SignedInAs(role).Can(permission));
}
