using System.Runtime.CompilerServices;
using System.Security.Claims;
using Ats.Web.Controllers;
using Ats.Web.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Ats.Tests.Authorization;

// A user signed in with a temporary password is held on Profile/ChangePassword until they change it.
public class RequirePasswordChangeFilterTests
{
    private static ActionExecutingContext Context(Type controller, bool mustChange = true, string? area = null,
        bool htmx = false, bool boosted = false)
    {
        var claims = mustChange ? new[] { new Claim(AtsSignIn.MustChangePasswordClaim, "true") } : [];
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, AtsSignIn.Scheme)) };
        if (htmx) http.Request.Headers["HX-Request"] = "true";
        if (boosted) http.Request.Headers["HX-Boosted"] = "true";
        var route = new RouteData();
        if (area is not null) route.Values["area"] = area;
        return new ActionExecutingContext(new ActionContext(http, route, new ActionDescriptor()), [],
            new Dictionary<string, object?>(), RuntimeHelpers.GetUninitializedObject(controller));
    }

    private static ActionExecutingContext Run(ActionExecutingContext ctx)
    {
        new RequirePasswordChangeFilter(new FakeLinkGenerator()).OnActionExecuting(ctx);
        return ctx;
    }

    private static void AssertRedirectsToChangePassword(IActionResult? result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ChangePassword", redirect.ActionName);
        Assert.Equal("Profile", redirect.ControllerName);
    }

    [Fact]
    public void User_without_the_claim_is_not_held() =>
        Assert.Null(Run(Context(typeof(JobsController), mustChange: false)).Result);

    [Fact]
    public void Back_office_page_redirects_to_change_password() =>
        AssertRedirectsToChangePassword(Run(Context(typeof(JobsController))).Result);

    [Theory]
    [InlineData(typeof(ProfileController))]
    [InlineData(typeof(AccountController))]
    [InlineData(typeof(HomeController))]
    public void Change_password_sign_out_and_status_pages_stay_reachable(Type controller) =>
        Assert.Null(Run(Context(controller)).Result);

    [Fact]
    public void Named_area_stays_reachable() =>
        Assert.Null(Run(Context(typeof(JobsController), area: "Careers")).Result);

    [Fact]
    public void Empty_area_value_is_still_held() =>
        AssertRedirectsToChangePassword(Run(Context(typeof(JobsController), area: "")).Result);

    [Fact]
    public void Boosted_htmx_request_gets_the_normal_redirect() =>
        AssertRedirectsToChangePassword(Run(Context(typeof(JobsController), htmx: true, boosted: true)).Result);

    [Fact]
    public void Non_boosted_htmx_request_gets_a_full_navigation_via_HX_Redirect()
    {
        var ctx = Run(Context(typeof(SearchController), htmx: true));
        Assert.IsType<NoContentResult>(ctx.Result);
        Assert.Equal("/Profile/ChangePassword", ctx.HttpContext.Response.Headers["HX-Redirect"].ToString());
    }

    [Fact]
    public void Non_boosted_htmx_request_falls_back_to_the_normal_redirect_when_no_link_can_be_built()
    {
        var ctx = Context(typeof(SearchController), htmx: true);
        new RequirePasswordChangeFilter(new FakeLinkGenerator(canBuild: false)).OnActionExecuting(ctx);
        AssertRedirectsToChangePassword(ctx.Result);
        Assert.False(ctx.HttpContext.Response.Headers.ContainsKey("HX-Redirect"));
    }

    // Renders the conventional {controller}/{action} path for the values the filter asked for.
    private sealed class FakeLinkGenerator(bool canBuild = true) : LinkGenerator
    {
        private string? Path<TAddress>(TAddress address) =>
            canBuild && address is RouteValuesAddress a ? $"/{a.ExplicitValues["controller"]}/{a.ExplicitValues["action"]}" : null;

        public override string? GetPathByAddress<TAddress>(HttpContext httpContext, TAddress address, RouteValueDictionary values,
            RouteValueDictionary? ambientValues = null, PathString? pathBase = null, FragmentString fragment = default,
            LinkOptions? options = null) => Path(address);

        public override string? GetPathByAddress<TAddress>(TAddress address, RouteValueDictionary values,
            PathString pathBase = default, FragmentString fragment = default, LinkOptions? options = null) => Path(address);

        public override string? GetUriByAddress<TAddress>(HttpContext httpContext, TAddress address, RouteValueDictionary values,
            RouteValueDictionary? ambientValues = null, string? scheme = null, HostString? host = null, PathString? pathBase = null,
            FragmentString fragment = default, LinkOptions? options = null) => throw new NotSupportedException();

        public override string? GetUriByAddress<TAddress>(TAddress address, RouteValueDictionary values, string scheme,
            HostString host, PathString pathBase = default, FragmentString fragment = default, LinkOptions? options = null) =>
            throw new NotSupportedException();
    }
}
