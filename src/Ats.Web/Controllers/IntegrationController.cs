using Ats.Application.Auditing;
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
    private readonly IAuditLogger _audit;
    private readonly IVacancyFeedRepository _feed;

    public IntegrationController(IIntegrationSettingsService settings, IDeliveryLogService log,
        IAuditLogger audit, IVacancyFeedRepository feed)
    {
        _settings = settings; _log = log; _audit = audit; _feed = feed;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await _settings.GetAsync();
        var (_, total) = await _feed.GetPageAsync(1, 1);

        // Typed health model instead of ViewData string keys, and one grouped count query instead of
        // four (QUAL-3).
        var counts = await _settings.GetOutboxCountsAsync();
        ViewData["Health"] = new IntegrationHealthViewModel
        {
            FeedLastPulledAt = s.FeedLastPulledAt,
            Delivered = counts.Delivered,
            Failed = counts.Failed,
            Pending = counts.Pending,
            RecentDeliveries = (await _log.SearchAsync(null, 1, 8)).Items
        };

        return View(new IntegrationSettingsViewModel
        {
            IntegrationEnabled = s.IntegrationEnabled,
            ReferralToolBaseUrl = s.ReferralToolBaseUrl,
            ReferralToolCustomerId = s.ReferralToolCustomerId,
            CodeParameterName = s.CodeParameterName,
            HasAuthToken = !string.IsNullOrEmpty(s.ReferralToolAuthToken),
            HasApiKey = !string.IsNullOrEmpty(s.ReferralToolApiKey),
            HasFeedKey = !string.IsNullOrEmpty(s.FeedApiKeyHash),
            PublishedJobCount = total,
            RowVersion = s.RowVersion
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(IntegrationSettingsViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var saved = await _settings.UpdateAsync(new IntegrationSettingsInput(
            vm.IntegrationEnabled, vm.ReferralToolBaseUrl, vm.ReferralToolCustomerId,
            vm.CodeParameterName, vm.ReferralToolAuthToken, vm.ReferralToolApiKey, vm.RowVersion));
        if (!saved)
        {
            TempData["Error"] = "These settings were changed by someone else. Reload the page and try again.";
            return RedirectToAction(nameof(Index));
        }
        await _audit.LogAsync("IntegrationSettingsSaved", "TenantSettings", null, "Updated integration settings");
        TempData["Success"] = "Integration settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> GenerateFeedKey()
    {
        var key = await _settings.GenerateFeedKeyAsync();
        await _audit.LogAsync("FeedKeyRegenerated", "TenantSettings", null, "Regenerated feed API key");
        TempData["FeedKey"] = key;   // shown once
        TempData["Success"] = "New feed API key generated. Copy it now; it will not be shown again.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> TestConnection()
    {
        // Validation, sample-vacancy selection and HTTP interpretation live in the service (QUAL-3).
        var test = await _settings.TestConnectionAsync();
        TempData[test.Succeeded ? "Success" : "Error"] = test.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Deliveries(OutboxStatus? status, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _log.SearchAsync(status, page, 20);
        return View(new DeliveryLogViewModel { Results = results, Status = status });
    }
}
