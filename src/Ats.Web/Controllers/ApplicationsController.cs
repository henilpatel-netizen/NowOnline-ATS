using Ats.Application.Applications;
using Ats.Application.Auditing;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly IApplicationService _service;
    private readonly IApplicationCardQuery _card;
    private readonly IAuditLogger _audit;

    public ApplicationsController(IApplicationService service, IApplicationCardQuery card, IAuditLogger audit)
    {
        _service = service;
        _card = card;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        // One targeted query with the candidate attached, instead of listing every application on
        // the job to find this one and then mutating the entity here (QUAL-7).
        var app = await _service.GetWithCandidateAsync(id);
        if (app is null) return NotFound();
        var stages = await _service.GetStagesForJobAsync(app.JobId);
        var events = await _service.ListEventsAsync(id);
        var name = app.Candidate?.FullName ?? "(unknown)";
        var card = await _card.GetAsync(id);
        return View(new ApplicationDetailsViewModel
        {
            Application = app,
            CandidateName = name,
            Stages = stages,
            Events = events,
            Card = card
        });
    }

    // Drawer body, loaded by htmx into #ats-drawer-host from the board.
    [HttpGet]
    public async Task<IActionResult> Card(int id, CancellationToken ct)
    {
        var card = await _card.GetAsync(id, ct);
        if (card is null) return NotFound();
        return PartialView("Partials/_CandidateDrawer", card);
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int id)
    {
        var app = await _service.GetAsync(id);
        if (app is null) return NotFound();
        var result = await _service.RemoveAsync(id);
        if (result.Succeeded)
            await _audit.LogAsync("ApplicationRemoved", "Application", id.ToString(),
                $"Removed application {id} (candidate {app.CandidateId}, job {app.JobId})");
        this.SetResultMessage(result, "Candidate removed from this job.");
        return RedirectToAction("Index", "Board", new { jobId = app.JobId });
    }
}
