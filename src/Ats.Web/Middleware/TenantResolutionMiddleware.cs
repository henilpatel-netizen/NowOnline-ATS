using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Middleware;

// For career-site requests (any request with a {slug} route value), resolves the slug to a TenantId
// and stores it in HttpContext.Items, which HttpTenantContext prefers over the tenant_id claim. This
// runs for signed-in users too, so the slug and the tenant always agree. Unknown or suspended slug
// returns 404 for everyone.
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AtsDbContext db)
    {
        if (context.GetRouteValue("slug") is string slug && slug.Length > 0)
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
