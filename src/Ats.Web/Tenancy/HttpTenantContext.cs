using Ats.Application.Abstractions;

namespace Ats.Web.Tenancy;

// Lives in the web host, not shared Infrastructure (QUAL-6): how a tenant is resolved is a
// host concern. The back office reads the tenant_id claim; the public career site is resolved from
// the {slug} route by TenantResolutionMiddleware, which stashes it in HttpContext.Items.
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? CurrentTenantId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            var claim = ctx?.User?.FindFirst("tenant_id")?.Value;
            if (int.TryParse(claim, out var id)) return id;

            // Set by TenantResolutionMiddleware for public career-site (slug) requests.
            if (ctx is not null && ctx.Items.TryGetValue("TenantId", out var v) && v is int tid) return tid;

            return null;
        }
    }

    public bool HasTenant => CurrentTenantId is not null;
}
