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
    private readonly IReferralToolClient _client;

    public IntegrationController(IIntegrationSettingsService settings, IDeliveryLogService log, IAuditLogger audit,
        IVacancyFeedRepository feed, IReferralToolClient client)
    {
        _settings = settings; _log = log; _audit = audit; _feed = feed; _client = client;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await _settings.GetAsync();
        var (_, total) = await _feed.GetPageAsync(1, 1);
        return View(new IntegrationSettingsViewModel
        {
            IntegrationEnabled = s.IntegrationEnabled,
            ReferralToolBaseUrl = s.ReferralToolBaseUrl,
            ReferralToolCustomerId = s.ReferralToolCustomerId,
            CodeParameterName = s.CodeParameterName,
            HasAuthToken = !string.IsNullOrEmpty(s.ReferralToolAuthToken),
            HasApiKey = !string.IsNullOrEmpty(s.ReferralToolApiKey),
            HasFeedKey = !string.IsNullOrEmpty(s.FeedApiKeyHash),
            PublishedJobCount = total
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(IntegrationSettingsViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _settings.UpdateAsync(new IntegrationSettingsInput(
            vm.IntegrationEnabled, vm.ReferralToolBaseUrl, vm.ReferralToolCustomerId,
            vm.CodeParameterName, vm.ReferralToolAuthToken, vm.ReferralToolApiKey));
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
        var s = await _settings.GetAsync();
        if (s.ReferralToolCustomerId is null || string.IsNullOrWhiteSpace(s.ReferralToolBaseUrl)
            || string.IsNullOrWhiteSpace(s.ReferralToolApiKey) || string.IsNullOrWhiteSpace(s.ReferralToolAuthToken))
        {
            TempData["Error"] = "Fill base URL, customer id, X-Api-Key, and X-Auth-Token first.";
            return RedirectToAction(nameof(Index));
        }

        var (page, _) = await _feed.GetPageAsync(1, 1);
        var sampleRef = page.FirstOrDefault()?.ExternalRef;
        if (sampleRef is null)
        {
            TempData["Error"] = "Publish a job first so there is a vacancy to test with.";
            return RedirectToAction(nameof(Index));
        }

        var settings = new ReferralToolSettings(s.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId.Value);
        var (result, exists) = await _client.CheckVacancyExistsAsync(settings, sampleRef);
        TempData[result.Reached && result.HttpStatus is >= 200 and < 300 ? "Success" : "Error"] =
            $"Test for {sampleRef}: reached={result.Reached}, HTTP {result.HttpStatus}, vacancy exists={exists}.";
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
