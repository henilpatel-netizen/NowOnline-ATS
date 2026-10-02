using System.Security.Claims;
using Ats.Application.Abstractions;
using Ats.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ats.Tests.Authorization;

// The per-request check that makes role changes, deactivation and password resets take effect at once.
public class SessionValidatorTests
{
    private static readonly Guid Stamp = Guid.NewGuid();

    [Fact]
    public void Active_user_with_matching_stamp_is_current() =>
        Assert.True(SessionValidator.IsCurrent(new UserSession(true, Stamp, "Viewer"), Stamp.ToString()));

    [Fact]
    public void Rotated_stamp_is_rejected() =>
        Assert.False(SessionValidator.IsCurrent(new UserSession(true, Guid.NewGuid(), "Viewer"), Stamp.ToString()));

    [Fact]
    public void Inactive_user_is_rejected() =>
        Assert.False(SessionValidator.IsCurrent(new UserSession(false, Stamp, "Viewer"), Stamp.ToString()));

    [Fact]
    public void Missing_user_is_rejected() =>
        Assert.False(SessionValidator.IsCurrent(null, Stamp.ToString()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void Cookie_without_a_valid_stamp_is_rejected(string? claim) =>
        Assert.False(SessionValidator.IsCurrent(new UserSession(true, Stamp, "Viewer"), claim));

    [Fact]
    public async Task Anonymous_endpoint_skips_the_lookup()
    {
        var (ctx, identity, auth) = Context(Claims("7", "3", Stamp.ToString()), anonymous: true);
        await SessionValidator.ValidateAsync(ctx);
        Assert.Equal(0, identity.Lookups);
        Assert.NotNull(ctx.Principal);
        Assert.Equal(0, auth.SignOuts);
    }

    [Theory]
    [InlineData(null, "3")]
    [InlineData("abc", "3")]
    [InlineData("7", null)]
    [InlineData("7", "abc")]
    public async Task Missing_or_unparsable_ids_reject_and_sign_out(string? userId, string? tenantId)
    {
        var (ctx, identity, auth) = Context(Claims(userId, tenantId, Stamp.ToString()));
        await SessionValidator.ValidateAsync(ctx);
        Assert.Null(ctx.Principal);
        Assert.Equal(1, auth.SignOuts);
        Assert.Equal(0, identity.Lookups);
    }

    [Fact]
    public async Task Stale_stamp_rejects_and_signs_out()
    {
        var (ctx, identity, auth) = Context(Claims("7", "3", Guid.NewGuid().ToString()));
        await SessionValidator.ValidateAsync(ctx);
        Assert.Null(ctx.Principal);
        Assert.Equal(1, auth.SignOuts);
        Assert.Equal((7, 3), identity.LastLookup);
    }

    [Fact]
    public async Task Current_session_passes()
    {
        var (ctx, identity, auth) = Context(Claims("7", "3", Stamp.ToString()));
        await SessionValidator.ValidateAsync(ctx);
        Assert.NotNull(ctx.Principal);
        Assert.Equal(0, auth.SignOuts);
        Assert.Equal((7, 3), identity.LastLookup);
    }

    private static ClaimsPrincipal Claims(string? userId, string? tenantId, string stamp)
    {
        var claims = new List<Claim> { new(AtsSignIn.StampClaim, stamp) };
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (tenantId is not null) claims.Add(new Claim(AtsSignIn.TenantClaim, tenantId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AtsSignIn.Scheme));
    }

    private static (CookieValidatePrincipalContext Ctx, FakeIdentity Identity, FakeAuthentication Auth) Context(
        ClaimsPrincipal principal, bool anonymous = false)
    {
        var identity = new FakeIdentity(new UserSession(true, Stamp, "Viewer"));
        var auth = new FakeAuthentication();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IIdentityService>(identity)
                .AddSingleton<IAuthenticationService>(auth)
                .BuildServiceProvider()
        };
        http.SetEndpoint(new Endpoint(null,
            anonymous ? new EndpointMetadataCollection(new AllowAnonymousAttribute()) : EndpointMetadataCollection.Empty, "test"));
        var scheme = new AuthenticationScheme(AtsSignIn.Scheme, null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, AtsSignIn.Scheme);
        return (new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), ticket), identity, auth);
    }

    private sealed class FakeIdentity(UserSession session) : IIdentityService
    {
        public int Lookups { get; private set; }
        public (int UserId, int TenantId)? LastLookup { get; private set; }

        public Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default)
        {
            Lookups++;
            LastLookup = (userId, tenantId);
            return Task.FromResult<UserSession?>(session);
        }

        public Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public string HashPassword(string password) => throw new NotSupportedException();
        public bool VerifyPassword(string hash, string password) => throw new NotSupportedException();
    }

    private sealed class FakeAuthentication : IAuthenticationService
    {
        public int SignOuts { get; private set; }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignOuts++;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
