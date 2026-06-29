using Ats.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Ats.Infrastructure.Tenancy;

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
