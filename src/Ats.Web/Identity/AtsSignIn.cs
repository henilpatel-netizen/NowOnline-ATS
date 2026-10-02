using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Ats.Web.Identity;

public static class AtsSignIn
{
    public const string Scheme = "AtsCookie";
    public const string TenantClaim = "tenant_id";
    public const string StampClaim = "security_stamp";
    public const string MustChangePasswordClaim = "must_change_password";

    public static Task SignInAsync(HttpContext http, int userId, int tenantId, string role, string displayName,
        Guid securityStamp, bool mustChangePassword)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, role),
            new(TenantClaim, tenantId.ToString(CultureInfo.InvariantCulture)),
            new(StampClaim, securityStamp.ToString()),
        };
        if (mustChangePassword) claims.Add(new(MustChangePasswordClaim, "true"));
        return http.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)));
    }
}
