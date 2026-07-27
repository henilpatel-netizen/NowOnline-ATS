using Ats.Application.Applications;
using Ats.Application.Candidates;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Controllers;

[Authorize]
public class BoardController : Controller
{
    private readonly IApplicationService _service;
    private readonly ICandidateService _candidates;

    public BoardController(IApplicationService service, ICandidateService candidates)
    {
        _service = service;
        _candidates = candidates;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int jobId)
    {
        var model = await BuildBoardAsync(jobId, null);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Move(int jobId, int applicationId, int toStageId, string rowVersion)
    {
        byte[] rv;
        try { rv = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { rv = Array.Empty<byte>(); }

        var result = await _service.MoveStageAsync(applicationId, toStageId, rv);
        var model = await BuildBoardAsync(jobId, result.Succeeded ? null : result.Error);
        if (model is null) return NotFound();

        if (Request.Headers.ContainsKey("HX-Request"))
            return PartialView("_Board", model);

        if (!result.Succeeded) TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Index), new { jobId });
    }

    [HttpPost]
    public async Task<IActionResult> AddCandidate(AddCandidateViewModel vm)
    {
        Ats.Application.Departments.OperationResult result;
        if (vm.CandidateId is int candidateId)
        {
            result = await _service.AddExistingCandidateToJobAsync(vm.JobId, candidateId);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(vm.FirstName) || string.IsNullOrWhiteSpace(vm.LastName) || string.IsNullOrWhiteSpace(vm.Email))
            {
                TempData["Error"] = "Pick an existing candidate, or fill first name, last name, and email for a new one.";
                return RedirectToAction(nameof(Index), new { jobId = vm.JobId });
            }
            result = await _service.AddCandidateToJobAsync(
                new AddCandidateToJobInput(vm.JobId, vm.FirstName!, vm.LastName!, vm.Email!, vm.Phone));
        }
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Candidate added to job." : result.Error;
        return RedirectToAction(nameof(Index), new { jobId = vm.JobId });
    }

    private async Task<BoardViewModel?> BuildBoardAsync(int jobId, string? error)
    {
        var job = await _service.GetJobAsync(jobId);
        if (job is null) return null;
        var stages = await _service.GetStagesForJobAsync(jobId);
        var apps = await _service.ListForJobAsync(jobId);
        var columns = stages.Select(s => new BoardColumn(s,
            apps.Where(a => a.CurrentStageId == s.Id)
                .Select(a => new BoardCard(a.Id,
                    a.Candidate?.FullName ?? "(unknown)",
                    Convert.ToBase64String(a.RowVersion))).ToList())).ToList();

        var candidateOptions = (await _candidates.ListAsync())
            .Select(c => new SelectListItem($"{c.FullName} <{c.Email}>", c.Id.ToString())).ToList();

        return new BoardViewModel { Job = job, Columns = columns, Error = error, CandidateOptions = candidateOptions };
    }
}
