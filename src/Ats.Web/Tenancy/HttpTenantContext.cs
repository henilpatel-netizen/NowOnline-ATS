using Ats.Application.Abstractions;

namespace Ats.Web.Tenancy;

// Lives in the web host, not shared Infrastructure (QUAL-6): how a tenant is resolved is a
// host concern. A career-site (slug) request is resolved by TenantResolutionMiddleware, which stashes
// the tenant in HttpContext.Items; that wins over the tenant_id claim so a signed-in user browsing
// another tenant's career site sees that tenant. Everything else (the back office) reads the claim.
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? CurrentTenantId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is null) return null;

            if (ctx.Items.TryGetValue("TenantId", out var v) && v is int tid) return tid;

            var claim = ctx.User?.FindFirst("tenant_id")?.Value;
            return int.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool HasTenant => CurrentTenantId is not null;
}
