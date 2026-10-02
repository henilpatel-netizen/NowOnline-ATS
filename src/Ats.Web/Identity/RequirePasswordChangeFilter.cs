using Ats.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ats.Web.Identity;

// Holds a user who signed in with a temporary password on the change-password page. Sign-out, error
// pages and the public career site stay reachable.
public sealed class RequirePasswordChangeFilter : IActionFilter
{
    private readonly LinkGenerator _links;
    public RequirePasswordChangeFilter(LinkGenerator links) => _links = links;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.User.FindFirst(AtsSignIn.MustChangePasswordClaim) is null) return;
        if (context.Controller is ProfileController or AccountController or HomeController) return;
        // Non-area actions can carry an empty area value, so only a named area counts.
        if (context.RouteData.Values.TryGetValue("area", out var area) && !string.IsNullOrEmpty(area?.ToString())) return;

        // A non-boosted htmx request (top-bar search) would follow a 302 and swap the whole change page
        // into its own target, so ask htmx for a full navigation instead.
        var headers = context.HttpContext.Request.Headers;
        if (headers["HX-Request"] == "true" && headers["HX-Boosted"] != "true"
            && _links.GetPathByAction(context.HttpContext, nameof(ProfileController.ChangePassword), "Profile") is { } url)
        {
            context.HttpContext.Response.Headers["HX-Redirect"] = url;
            context.Result = new NoContentResult();
            return;
        }
        context.Result = new RedirectToActionResult(nameof(ProfileController.ChangePassword), "Profile", null);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
