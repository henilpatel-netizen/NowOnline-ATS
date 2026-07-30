using Ats.Application.Branding;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public class BrandingViewComponent : ViewComponent
{
    private readonly ITenantBrandingService _branding;
    public BrandingViewComponent(ITenantBrandingService branding) => _branding = branding;

    public async Task<IViewComponentResult> InvokeAsync() => View(await _branding.GetAsync());
}
