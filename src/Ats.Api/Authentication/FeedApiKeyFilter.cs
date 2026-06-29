using Ats.Application.Integration;
using Ats.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Ats.Api.Authentication;

// Resolves the tenant from an `Authorization: Token {feedKey}` header by matching the SHA-256 hash
// against TenantSettings.FeedApiKeyHash (an IgnoreQueryFilters lookup, since no tenant is resolved
// yet), then sets HttpContext.Items["TenantId"] so the global query filter scopes the feed query.
public sealed class FeedApiKeyFilter : IAsyncAuthorizationFilter
{
    private const string Scheme = "Token ";
    private readonly AtsDbContext _db;

    public FeedApiKeyFilter(AtsDbContext db) => _db = db;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Scheme, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var key = header[Scheme.Length..].Trim();
        if (key.Length == 0)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var hash = FeedApiKey.Hash(key);
        var settings = await _db.TenantSettings.IgnoreQueryFilters()
            .Where(s => s.FeedApiKeyHash == hash)
            .Select(s => new { s.TenantId })
            .FirstOrDefaultAsync();

        if (settings is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        context.HttpContext.Items["TenantId"] = settings.TenantId;
    }
}
