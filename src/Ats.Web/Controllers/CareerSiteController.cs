using Ats.Application.Branding;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

// Named CareerSite (not Careers) so it cannot collide with the public site's attribute route
// `careers/{slug}`; a literal route segment wins over a conventional one.
[Authorize]
public class CareerSiteController : Controller
{
    private readonly ITenantBrandingService _branding;
    public CareerSiteController(ITenantBrandingService branding) => _branding = branding;

    public async Task<IActionResult> Index() => View(await _branding.GetAsync()); // model: TenantBranding

    [HttpGet]
    [Authorize(Roles = AtsRole.Owner)]
    public async Task<IActionResult> Branding()
    {
        var b = await _branding.GetAsync();
        return View(new BrandingEditViewModel
        {
            AccentColor = b.Accent,
            SidebarTheme = b.SidebarTheme,
            CareerHeroHeadline = b.CareerHeroHeadline,
            CareerHeroHeadlineOutlined = b.CareerHeroHeadlineOutlined,
            CareerHeroIntro = b.CareerHeroIntro,
            TenantName = b.TenantName,
            TenantSlug = b.TenantSlug
        });
    }

    [HttpPost]
    [Authorize(Roles = AtsRole.Owner)]
    public async Task<IActionResult> Branding(BrandingEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _branding.UpdateAsync(new BrandingInput(
            vm.AccentColor, vm.SidebarTheme, vm.CareerHeroHeadline, vm.CareerHeroHeadlineOutlined, vm.CareerHeroIntro));
        TempData["Success"] = "Branding saved.";
        return RedirectToAction(nameof(Index));
    }
}
