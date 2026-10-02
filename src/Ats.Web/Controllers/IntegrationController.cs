using Ats.Application.Auditing;
using Ats.Application.Integration;
using Ats.Domain.Authorization;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Policy = AtsPermission.IntegrationManage)]
public class IntegrationController : Controller
{
    private readonly IIntegrationSettingsService _settings;
    private readonly IDeliveryLogService _log;
    private readonly IAuditLogger _audit;

    public IntegrationController(IIntegrationSettingsService settings, IDeliveryLogService log,
        IAuditLogger audit)
    {
        _settings = settings; _log = log; _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await _settings.GetAsync();

        // Typed health model instead of ViewData string keys, and one grouped count query instead of
        // four (QUAL-3).
        var counts = await _settings.GetOutboxCountsAsync();
        ViewData["Health"] = new IntegrationHealthViewModel
        {
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
            PublishedJobCount = await _settings.CountPublishedJobsAsync(),
            RowVersion = s.RowVersion
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(IntegrationSettingsViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _settings.UpdateAsync(new IntegrationSettingsInput(
            vm.IntegrationEnabled, vm.ReferralToolBaseUrl, vm.ReferralToolCustomerId,
            vm.CodeParameterName, vm.ReferralToolAuthToken, vm.ReferralToolApiKey, vm.RowVersion));
        if (result.Succeeded)
            await _audit.LogAsync("IntegrationSettingsSaved", "TenantSettings", null, "Updated integration settings");
        this.SetResultMessage(result, "Integration settings saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SyncVacancies()
    {
        var queued = await _settings.QueueVacancySyncAsync();
        if (queued > 0)
            await _audit.LogAsync("VacancySyncQueued", "TenantSettings", null, $"Queued {queued} vacancy sync(s)");
        TempData[queued > 0 ? "Success" : "Error"] = queued switch
        {
            null => "Nothing queued: enable and complete the integration settings first.",
            0 => "Nothing queued: there are no published or closed jobs yet.",
            _ => $"Queued {queued} vacancy sync(s) to ReferralTool."
        };
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
        var results = await _log.SearchAsync(status, page, 20);
        return View(new DeliveryLogViewModel { Results = results, Status = status });
    }
}
