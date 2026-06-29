using Ats.Application.Auditing;
using Ats.Application.Departments;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class DepartmentsController : Controller
{
    private readonly IDepartmentService _service;
    private readonly IAuditLogger _audit;
    public DepartmentsController(IDepartmentService service, IAuditLogger audit)
    {
        _service = service; _audit = audit;
    }

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet] public IActionResult Create() => View("Form", new DepartmentViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(DepartmentViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.CreateAsync(vm.Name);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        await _audit.LogAsync("DepartmentCreated", "Department", null, $"Created department '{vm.Name}'");
        TempData["Success"] = "Department created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var d = await _service.GetAsync(id);
        if (d is null) return NotFound();
        return View("Form", new DepartmentViewModel { Id = d.Id, Name = d.Name });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(DepartmentViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.UpdateAsync(vm.Id, vm.Name);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        await _audit.LogAsync("DepartmentUpdated", "Department", vm.Id.ToString(), $"Updated department '{vm.Name}'");
        TempData["Success"] = "Department updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (result.Succeeded) await _audit.LogAsync("DepartmentDeleted", "Department", id.ToString(), $"Deleted department {id}");
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Department deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
