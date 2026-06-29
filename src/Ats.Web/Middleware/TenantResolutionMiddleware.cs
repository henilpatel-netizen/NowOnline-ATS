using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Middleware;

// For public career-site requests (no auth), resolves the {slug} route value to a TenantId and
// stores it in HttpContext.Items so the global query filter scopes the request. Unknown or
// suspended slug returns 404.
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AtsDbContext db)
    {
        var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;
        if (!isAuthenticated && context.GetRouteValue("slug") is string slug && slug.Length > 0)
        {
            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug == slug && t.Status == TenantStatus.Active);
            if (tenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Items["TenantId"] = tenant.Id;
        }

        await _next(context);
    }
}
