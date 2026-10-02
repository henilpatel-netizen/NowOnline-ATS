using System.Security.Claims;
using Ats.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Ats.Web.Identity;

// Checks the cookie against the database on every non-anonymous request (one indexed lookup; there is
// no server-side cache by design). A role change, deactivation, password reset or tenant suspension
// therefore signs the user out on their next request.
public static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        // Static assets, health checks, sign-in and the career site do not use the user (the career site
        // resolves its tenant from the slug, not the cookie); skip the lookup.
        if (ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null) return;

        var principal = ctx.Principal;
        if (principal is null
            || !int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            || !int.TryParse(principal.FindFirst(AtsSignIn.TenantClaim)?.Value, out var tenantId))
        {
            await RejectAsync(ctx);
            return;
        }

        var identity = ctx.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
        var session = await identity.GetSessionAsync(userId, tenantId, ctx.HttpContext.RequestAborted);
        if (!IsCurrent(session, principal.FindFirst(AtsSignIn.StampClaim)?.Value))
            await RejectAsync(ctx);
    }

    public static bool IsCurrent(UserSession? session, string? stampClaim) =>
        session is { IsActive: true }
        && Guid.TryParse(stampClaim, out var stamp)
        && stamp == session.SecurityStamp;

    private static async Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(AtsSignIn.Scheme);
    }
}
