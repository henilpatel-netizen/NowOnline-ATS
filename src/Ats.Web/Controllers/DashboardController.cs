using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    public IActionResult Index() => View();
}
