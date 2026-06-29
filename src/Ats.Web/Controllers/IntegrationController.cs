using Ats.Application.Integration;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Roles = AtsRole.Owner)]
public class IntegrationController : Controller
{
    private readonly IIntegrationSettingsService _settings;
    private readonly IDeliveryLogService _log;

    public IntegrationController(IIntegrationSettingsService settings, IDeliveryLogService log)
    {
        _settings = settings; _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await _settings.GetAsync();
        return View(new IntegrationSettingsViewModel
        {
            IntegrationEnabled = s.IntegrationEnabled,
            ReferralToolBaseUrl = s.ReferralToolBaseUrl,
            ReferralToolCustomerId = s.ReferralToolCustomerId,
            CodeParameterName = s.CodeParameterName,
            HasAuthToken = !string.IsNullOrEmpty(s.ReferralToolAuthToken),
            HasApiKey = !string.IsNullOrEmpty(s.ReferralToolApiKey),
            HasFeedKey = !string.IsNullOrEmpty(s.FeedApiKeyHash)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(IntegrationSettingsViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _settings.UpdateAsync(new IntegrationSettingsInput(
            vm.IntegrationEnabled, vm.ReferralToolBaseUrl, vm.ReferralToolCustomerId,
            vm.CodeParameterName, vm.ReferralToolAuthToken, vm.ReferralToolApiKey));
        TempData["Success"] = "Integration settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> GenerateFeedKey()
    {
        var key = await _settings.GenerateFeedKeyAsync();
        TempData["FeedKey"] = key;   // shown once
        TempData["Success"] = "New feed API key generated. Copy it now; it will not be shown again.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Deliveries()
    {
        return View(await _log.RecentAsync());
    }
}
