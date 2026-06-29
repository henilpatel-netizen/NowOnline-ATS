using Ats.Application.Applications;
using Ats.Application.Auditing;
using Ats.Application.Candidates;
using Ats.Application.Jobs;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Controllers;

[Authorize]
public class CandidatesController : Controller
{
    private readonly ICandidateService _service;
    private readonly IJobService _jobs;
    private readonly IApplicationService _applications;
    private readonly IAuditLogger _audit;

    public CandidatesController(ICandidateService service, IJobService jobs, IApplicationService applications, IAuditLogger audit)
    {
        _service = service; _jobs = jobs; _applications = applications; _audit = audit;
    }

    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _service.SearchAsync(q, page, 20);
        var jobs = (await _jobs.ListAsync())
            .Where(j => j.Status == JobStatus.Published)
            .Select(j => new SelectListItem($"{j.ExternalRef} - {j.Title}", j.Id.ToString())).ToList();
        return View(new CandidatesIndexViewModel { Results = results, Q = q, PublishedJobs = jobs });
    }

    [HttpGet] public IActionResult Create() => View("Form", new CandidateViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CandidateViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.CreateAsync(vm.FirstName, vm.LastName, vm.Email, vm.Phone);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        await _audit.LogAsync("CandidateCreated", "Candidate", null, $"Created candidate '{vm.FirstName} {vm.LastName}'");
        TempData["Success"] = "Candidate created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await _service.GetAsync(id);
        if (c is null) return NotFound();
        return View("Form", new CandidateViewModel { Id = c.Id, FirstName = c.FirstName, LastName = c.LastName, Email = c.Email, Phone = c.Phone });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(CandidateViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.UpdateAsync(vm.Id, vm.FirstName, vm.LastName, vm.Email, vm.Phone);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        await _audit.LogAsync("CandidateUpdated", "Candidate", vm.Id.ToString(), $"Updated candidate '{vm.FirstName} {vm.LastName}'");
        TempData["Success"] = "Candidate updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddToJob(int candidateId, int jobId)
    {
        if (jobId == 0) { TempData["Error"] = "Pick a job."; return RedirectToAction(nameof(Index)); }
        var result = await _applications.AddExistingCandidateToJobAsync(jobId, candidateId);
        if (!result.Succeeded) { TempData["Error"] = result.Error; return RedirectToAction(nameof(Index)); }
        TempData["Success"] = "Candidate added to job.";
        return RedirectToAction("Index", "Board", new { jobId });
    }
}
