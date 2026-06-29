using System.Security.Claims;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public record NavItem(string Text, string Icon, string Controller, string Action, string? RequiredRole = null);

public record SidebarNavModel(IReadOnlyList<NavItem> Items, string CurrentController, string UserName, string Role);

public class SidebarNavViewComponent : ViewComponent
{
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "bi-speedometer2", "Dashboard", "Index"),
        new("Jobs", "bi-briefcase", "Jobs", "Index"),
        new("Pipelines", "bi-diagram-3", "Pipelines", "Index"),
        new("Candidates", "bi-people", "Candidates", "Index"),
        new("Departments", "bi-building", "Departments", "Index"),
        new("Locations", "bi-geo-alt", "Locations", "Index"),
        new("Integration", "bi-plugin", "Integration", "Index", AtsRole.Owner),
    };

    public IViewComponentResult Invoke()
    {
        var current = RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var name = User.Identity?.Name is { Length: > 0 } n ? n : "User";
        var role = (User as ClaimsPrincipal)?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var visible = Items
            .Where(i => i.RequiredRole is null || string.Equals(i.RequiredRole, role, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return View(new SidebarNavModel(visible, current, name, role));
    }
}
