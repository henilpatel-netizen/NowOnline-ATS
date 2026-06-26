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
            var claim = _accessor.HttpContext?.User?.FindFirst("tenant_id")?.Value;
            return int.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool HasTenant => CurrentTenantId is not null;
}
