using System.Security.Claims;
using Ats.Application.Branding;
using Ats.Application.Shell;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public enum NavGroup { None, Hiring, Setup, Admin }

public record NavItem(
    string Text,
    string Icon,
    string Controller,
    string Action,
    NavGroup Group = NavGroup.None,
    string? RequiredRole = null,
    Func<ShellSummary, int?>? Count = null,
    Func<ShellSummary, bool>? Alert = null);

public record SidebarNavModel(
    IReadOnlyList<IGrouping<NavGroup, NavItem>> Groups,
    string CurrentController,
    string UserName,
    string Role,
    TenantBranding Branding,
    ShellSummary Summary);

public class SidebarNavViewComponent : ViewComponent
{
    // The single place a new nav entry is added. Group, badge count and alert flag are declared here.
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "space_dashboard", "Dashboard", "Index"),

        new("Jobs", "work_outline", "Jobs", "Index", NavGroup.Hiring, Count: s => s.OpenJobs),
        new("Candidates", "group", "Candidates", "Index", NavGroup.Hiring, Count: s => s.Candidates),

        new("Pipelines", "view_week", "Pipelines", "Index", NavGroup.Setup),
        new("Organisation", "apartment", "Organisation", "Index", NavGroup.Setup),
        new("Career site", "public", "CareerSite", "Index", NavGroup.Setup),

        new("Integrations", "cable", "Integration", "Index", NavGroup.Admin, AtsRole.Owner,
            Alert: s => s.IntegrationUnhealthy),
        new("Audit log", "history", "Audit", "Index", NavGroup.Admin, AtsRole.Owner),
    };

    private readonly ITenantBrandingService _branding;
    private readonly IShellSummaryService _summary;

    public SidebarNavViewComponent(ITenantBrandingService branding, IShellSummaryService summary)
    {
        _branding = branding;
        _summary = summary;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var current = RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var name = User.Identity?.Name is { Length: > 0 } n ? n : "User";
        var role = (User as ClaimsPrincipal)?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        var branding = await _branding.GetAsync();
        var summary = await _summary.GetAsync();

        var groups = Items
            .Where(i => i.RequiredRole is null || string.Equals(i.RequiredRole, role, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Group)
            .ToList();

        return View(new SidebarNavModel(groups, current, name, role, branding, summary));
    }
}
