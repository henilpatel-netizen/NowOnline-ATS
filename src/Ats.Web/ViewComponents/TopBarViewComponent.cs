using Ats.Application.Shell;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public record TopBarModel(string CrumbRoot, string CrumbLeaf, ShellSummary Summary);

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
        var crumb = Crumbs.TryGetValue(controller, out var c) ? c : (Root: "Overview", Leaf: controller);

        // ViewData["Title"] is the most specific label available, so prefer it for the leaf.
        var title = ViewData["Title"] as string;
        var leaf = string.IsNullOrWhiteSpace(title) ? crumb.Leaf : title;

        return View(new TopBarModel(crumb.Root, leaf, await _summary.GetAsync()));
    }
}
