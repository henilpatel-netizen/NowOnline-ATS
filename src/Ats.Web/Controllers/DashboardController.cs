using Ats.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    public async Task<IActionResult> Index() => View(await _dashboard.GetAsync());
}
