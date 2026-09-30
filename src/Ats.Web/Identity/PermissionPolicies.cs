using System.Security.Claims;
using Ats.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Ats.Web.Identity;

public static class PermissionPolicies
{
    // One policy per permission, named after it. The fallback policy makes any endpoint without
    // authorization metadata require a signed-in user, so a new controller is never public by accident.
    public static void AddAtsPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in AtsPermission.All)
            options.AddPolicy(permission, p => p.RequireRole(RolePermissions.RolesWith(permission)));

        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }

    // For views and view components: hides what the policy would refuse anyway.
    public static bool Can(this ClaimsPrincipal user, string permission) =>
        RolePermissions.Has(user.FindFirst(ClaimTypes.Role)?.Value, permission);
}
