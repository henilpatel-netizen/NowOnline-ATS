using Ats.Application.Applications;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly IApplicationService _service;
    public ApplicationsController(IApplicationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var app = await _service.GetAsync(id);
        if (app is null) return NotFound();
        var stages = await _service.GetStagesForJobAsync(app.JobId);
        var events = await _service.ListEventsAsync(id);
        var name = (await _service.ListForJobAsync(app.JobId))
            .FirstOrDefault(a => a.Id == id)?.Candidate?.FullName ?? "(unknown)";
        return View(new ApplicationDetailsViewModel
        {
            Application = app, CandidateName = name, Stages = stages, Events = events
        });
    }
}
