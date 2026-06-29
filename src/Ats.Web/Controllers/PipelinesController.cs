using Ats.Application.Pipelines;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class PipelinesController : Controller
{
    private readonly IPipelineTemplateService _service;
    public PipelinesController(IPipelineTemplateService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet]
    public IActionResult Create() => View("Form", new PipelineEditViewModel
    {
        Stages = new() { new StageRow { Name = "Applied", Order = 1 } }
    });

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var t = await _service.GetAsync(id);
        if (t is null) return NotFound();
        var vm = new PipelineEditViewModel
        {
            Id = t.Id,
            Name = t.Name,
            Stages = t.Stages.OrderBy(s => s.Order).Select(s => new StageRow
            {
                Id = s.Id, Name = s.Name, Order = s.Order, IsTerminal = s.IsTerminal,
                TerminalOutcome = s.TerminalOutcome, ReferralStatusOverride = s.ReferralStatusOverride
            }).ToList()
        };
        return View("Form", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Save(PipelineEditViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);

        var input = new PipelineTemplateInput(
            vm.Id,
            vm.Name,
            vm.Stages.Select(s => new StageInput(
                s.Id, s.Name, s.Order, s.IsTerminal, s.TerminalOutcome, s.ReferralStatusOverride, s.Delete)).ToList());

        var result = await _service.SaveAsync(input);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Pipeline saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Pipeline deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
