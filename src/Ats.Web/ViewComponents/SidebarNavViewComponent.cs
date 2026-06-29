using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public record NavItem(string Text, string Icon, string Controller, string Action);

public record SidebarNavModel(IReadOnlyList<NavItem> Items, string CurrentController, string UserName, string Role);

public class SidebarNavViewComponent : ViewComponent
{
    // Phase 1+ appends Jobs, Pipelines, Candidates, Settings here.
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "bi-speedometer2", "Dashboard", "Index"),
        new("Jobs", "bi-briefcase", "Jobs", "Index"),
        new("Pipelines", "bi-diagram-3", "Pipelines", "Index"),
        new("Departments", "bi-building", "Departments", "Index"),
        new("Locations", "bi-geo-alt", "Locations", "Index"),
    };

    public IViewComponentResult Invoke()
    {
        var current = RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var name = User.Identity?.Name is { Length: > 0 } n ? n : "User";
        var role = (User as ClaimsPrincipal)?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        return View(new SidebarNavModel(Items, current, name, role));
    }
}
