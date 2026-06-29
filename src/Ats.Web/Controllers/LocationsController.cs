using Ats.Application.Auditing;
using Ats.Application.Locations;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class LocationsController : Controller
{
    private readonly ILocationService _service;
    private readonly IAuditLogger _audit;
    public LocationsController(ILocationService service, IAuditLogger audit)
    {
        _service = service; _audit = audit;
    }

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet] public IActionResult Create() => View("Form", new LocationViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(LocationViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.CreateAsync(vm.Name, vm.City);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        await _audit.LogAsync("LocationCreated", "Location", null, $"Created location '{vm.Name}'");
        TempData["Success"] = "Location created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var l = await _service.GetAsync(id);
        if (l is null) return NotFound();
        return View("Form", new LocationViewModel { Id = l.Id, Name = l.Name, City = l.City });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(LocationViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.UpdateAsync(vm.Id, vm.Name, vm.City);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        await _audit.LogAsync("LocationUpdated", "Location", vm.Id.ToString(), $"Updated location '{vm.Name}'");
        TempData["Success"] = "Location updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (result.Succeeded) await _audit.LogAsync("LocationDeleted", "Location", id.ToString(), $"Deleted location {id}");
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Location deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
