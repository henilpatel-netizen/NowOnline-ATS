using Ats.Application.Shell;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

// ParentController is set only on a sub-page (anything that is not the section's Index), so the
// breadcrumb offers a real way back instead of three unclickable words.
public record TopBarModel(
    string CrumbRoot, string CrumbLeaf, ShellSummary Summary,
    string? ParentLabel = null, string? ParentController = null);

public class TopBarViewComponent : ViewComponent
{
    // Controller -> (group label, page label). Keep in step with SidebarNavViewComponent.Items.
    private static readonly Dictionary<string, (string Root, string Leaf)> Crumbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = ("Overview", "Dashboard"),
        ["Jobs"] = ("Hiring", "Jobs"),
        ["Board"] = ("Hiring", "Board"),
        ["Candidates"] = ("Hiring", "Candidates"),
        ["Applications"] = ("Hiring", "Application"),
        ["Pipelines"] = ("Setup", "Pipelines"),
        ["Organisation"] = ("Setup", "Organisation"),
        ["Departments"] = ("Setup", "Departments"),
        ["Locations"] = ("Setup", "Locations"),
        ["CareerSite"] = ("Setup", "Career site"),
        ["Integration"] = ("Admin", "Integrations"),
        ["Audit"] = ("Admin", "Audit log"),
    };

    private readonly IShellSummaryService _summary;
    public TopBarViewComponent(IShellSummaryService summary) => _summary = summary;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var controller = RouteData.Values["controller"]?.ToString() ?? "Dashboard";
        var action = RouteData.Values["action"]?.ToString() ?? "Index";
        var known = Crumbs.TryGetValue(controller, out var c);
        var crumb = known ? c : (Root: "Overview", Leaf: controller);

        // ViewData["Title"] is the most specific label available, so prefer it for the leaf.
        var title = ViewData["Title"] as string;
        var leaf = string.IsNullOrWhiteSpace(title) ? crumb.Leaf : title;

        // On a sub-page, name the section it belongs to and link back to it.
        var onSubPage = known && !string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase);
        return View(new TopBarModel(
            crumb.Root, leaf, await _summary.GetAsync(),
            onSubPage ? crumb.Leaf : null,
            onSubPage ? controller : null));
    }
}
