using System.Security.Claims;
using Ats.Web.Tenancy;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Ats.Tests.Tenancy;

public class HttpTenantContextTests
{
    private sealed class FakeAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static HttpTenantContext Create(int? itemTenant, int? claimTenant)
    {
        var http = new DefaultHttpContext();
        if (itemTenant is int item) http.Items["TenantId"] = item;
        if (claimTenant is int claim)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant_id", claim.ToString()) }, "test"));
        return new HttpTenantContext(new FakeAccessor { HttpContext = http });
    }

    [Fact]
    public void Items_only_resolves_the_item() => Assert.Equal(7, Create(itemTenant: 7, claimTenant: null).CurrentTenantId);

    [Fact]
    public void Claim_only_resolves_the_claim() => Assert.Equal(3, Create(itemTenant: null, claimTenant: 3).CurrentTenantId);

    [Fact]
    public void Slug_item_wins_over_the_signed_in_claim() => Assert.Equal(7, Create(itemTenant: 7, claimTenant: 3).CurrentTenantId);

    [Fact]
    public void Neither_resolves_no_tenant()
    {
        var ctx = Create(itemTenant: null, claimTenant: null);
        Assert.Null(ctx.CurrentTenantId);
        Assert.False(ctx.HasTenant);
    }

    [Fact]
    public void No_http_context_resolves_no_tenant() =>
        Assert.Null(new HttpTenantContext(new FakeAccessor()).CurrentTenantId);
}
