using Ats.Application.Abstractions;

namespace Ats.Api.Tenancy;

// The API resolves the tenant solely from the hashed feed key (FeedApiKeyFilter stashes it in
// HttpContext.Items). There are no claims on a feed request, so this deliberately does not look for
// any: each host owns a tenant context that matches how it actually resolves tenants (QUAL-6).
public sealed class FeedTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public FeedTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? CurrentTenantId =>
        _accessor.HttpContext is { } ctx && ctx.Items.TryGetValue("TenantId", out var v) && v is int id
            ? id : null;

    public bool HasTenant => CurrentTenantId is not null;
}
