using System.Security.Claims;
using Ats.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Ats.Infrastructure.Identity;

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
