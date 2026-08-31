using Ats.Application.Auditing;
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
    private readonly IJobListQuery _jobList;
    private readonly IDepartmentService _departments;
    private readonly ILocationService _locations;
    private readonly IPipelineTemplateService _pipelines;
    private readonly IAuditLogger _audit;

    public JobsController(IJobService jobs, IJobListQuery jobList, IDepartmentService departments,
        ILocationService locations, IPipelineTemplateService pipelines, IAuditLogger audit)
    {
        _jobs = jobs; _jobList = jobList; _departments = departments; _locations = locations; _pipelines = pipelines; _audit = audit;
    }

    public async Task<IActionResult> Index(string? q, Ats.Domain.Enums.JobStatus? status, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _jobList.SearchAsync(status, q, page, 20);
        return View(new JobsIndexViewModel { Results = results, Q = q, Status = status });
    }

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
        await _audit.LogAsync("JobCreated", "Job", null, $"Created job '{vm.Title}'");
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
            Id = job.Id,
            Title = job.Title,
            Description = job.Description,
            DepartmentId = job.DepartmentId,
            LocationId = job.LocationId,
            EmploymentType = job.EmploymentType,
            PipelineTemplateId = job.PipelineTemplateId
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
        await _audit.LogAsync("JobUpdated", "Job", vm.Id?.ToString(), $"Updated job '{vm.Title}'");
        TempData["Success"] = "Job updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Publish(int id)
    {
        var result = await _jobs.PublishAsync(id);
        if (result.Succeeded) await _audit.LogAsync("JobPublished", "Job", id.ToString(), $"Published job {id}");
        this.SetResultMessage(result, "Job published.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Close(int id)
    {
        var result = await _jobs.CloseAsync(id);
        if (result.Succeeded) await _audit.LogAsync("JobClosed", "Job", id.ToString(), $"Closed job {id}");
        this.SetResultMessage(result, "Job closed.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _jobs.DeleteAsync(id);
        if (result.Succeeded) await _audit.LogAsync("JobDeleted", "Job", id.ToString(), $"Deleted job {id}");
        this.SetResultMessage(result, "Job deleted.");
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
