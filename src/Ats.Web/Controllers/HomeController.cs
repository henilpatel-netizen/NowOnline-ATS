using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ats.Web.Models;

namespace Ats.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Dashboard");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        ViewData["Code"] = code;
        ViewData["Message"] = code switch
        {
            404 => "We could not find that page.",
            403 => "You do not have access to that.",
            _ => "Something went wrong."
        };
        Response.StatusCode = code;
        return View();
    }
}
