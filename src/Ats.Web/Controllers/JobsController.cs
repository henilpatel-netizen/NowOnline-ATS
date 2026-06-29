using Ats.Application.Departments;
using Ats.Application.Jobs;
using Ats.Application.Locations;
using Ats.Application.Pipelines;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Controllers;

[Authorize]
public class JobsController : Controller
{
    private readonly IJobService _jobs;
    private readonly IDepartmentService _departments;
    private readonly ILocationService _locations;
    private readonly IPipelineTemplateService _pipelines;

    public JobsController(IJobService jobs, IDepartmentService departments,
        ILocationService locations, IPipelineTemplateService pipelines)
    {
        _jobs = jobs; _departments = departments; _locations = locations; _pipelines = pipelines;
    }

    public async Task<IActionResult> Index() => View(await _jobs.ListAsync());

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new JobEditViewModel();
        await PopulateLists(vm);
        return View("Form", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Create(JobEditViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLists(vm); return View("Form", vm); }
        var result = await _jobs.CreateAsync(new JobInput(null, vm.Title, vm.Description,
            vm.DepartmentId, vm.LocationId, vm.EmploymentType, vm.PipelineTemplateId));
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); await PopulateLists(vm); return View("Form", vm); }
        TempData["Success"] = "Job created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var job = await _jobs.GetAsync(id);
        if (job is null) return NotFound();
        var vm = new JobEditViewModel
        {
            Id = job.Id, Title = job.Title, Description = job.Description,
            DepartmentId = job.DepartmentId, LocationId = job.LocationId,
            EmploymentType = job.EmploymentType, PipelineTemplateId = job.PipelineTemplateId
        };
        await PopulateLists(vm);
        return View("Form", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(JobEditViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLists(vm); return View("Form", vm); }
        var result = await _jobs.UpdateAsync(new JobInput(vm.Id, vm.Title, vm.Description,
            vm.DepartmentId, vm.LocationId, vm.EmploymentType, vm.PipelineTemplateId));
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); await PopulateLists(vm); return View("Form", vm); }
        TempData["Success"] = "Job updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost] public Task<IActionResult> Publish(int id) => Lifecycle(_jobs.PublishAsync(id), "Job published.");
    [HttpPost] public Task<IActionResult> Close(int id) => Lifecycle(_jobs.CloseAsync(id), "Job closed.");

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _jobs.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Job deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Lifecycle(Task<OperationResult> action, string okMessage)
    {
        var result = await action;
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? okMessage : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLists(JobEditViewModel vm)
    {
        vm.Departments = (await _departments.ListAsync())
            .Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToList();
        vm.Locations = (await _locations.ListAsync())
            .Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToList();
        vm.Pipelines = (await _pipelines.ListAsync())
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();
    }
}
