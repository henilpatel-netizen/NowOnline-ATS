using Ats.Application.Applications;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly IApplicationService _service;
    private readonly IApplicationCardQuery _card;

    public ApplicationsController(IApplicationService service, IApplicationCardQuery card)
    {
        _service = service;
        _card = card;
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var app = await _service.GetAsync(id);
        if (app is null) return NotFound();
        var stages = await _service.GetStagesForJobAsync(app.JobId);
        var events = await _service.ListEventsAsync(id);
        var withCandidate = (await _service.ListForJobAsync(app.JobId)).FirstOrDefault(a => a.Id == id);
        app.Candidate = withCandidate?.Candidate;
        var name = app.Candidate?.FullName ?? "(unknown)";
        var card = await _card.GetAsync(id);
        return View(new ApplicationDetailsViewModel
        {
            Application = app, CandidateName = name, Stages = stages, Events = events, Card = card
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
}
