using System.Security.Claims;
using Ats.Application.Abstractions;

namespace Ats.Web.Identity;

// Moved out of shared Infrastructure with HttpTenantContext (QUAL-6): a claims-based current user is
// a web concern. Neither the API nor the Worker has a signed-in user.
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? UserId =>
        int.TryParse(_accessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id : null;

    public string? Name => _accessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;

    public string? Role => _accessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;

    public bool IsAuthenticated => _accessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
