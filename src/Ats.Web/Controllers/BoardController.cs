using Ats.Application.Applications;
using Ats.Application.Common;
using Ats.Web.Models;
using Ats.Web.ViewServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class BoardController : Controller
{
    private readonly IApplicationService _service;
    private readonly IBoardViewService _board;

    public BoardController(IApplicationService service, IBoardViewService board)
    {
        _service = service;
        _board = board;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int jobId)
    {
        var model = await _board.BuildAsync(jobId, null);
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
        var model = await _board.BuildAsync(jobId, result.Succeeded ? null : result.Error);
        if (model is null) return NotFound();

        if (Request.Headers.ContainsKey("HX-Request"))
            return PartialView("_Board", model);

        if (!result.Succeeded) TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Index), new { jobId });
    }

    [HttpPost]
    public async Task<IActionResult> AddCandidate(AddCandidateViewModel vm)
    {
        OperationResult result;
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
        this.SetResultMessage(result, "Candidate added to job.");
        return RedirectToAction(nameof(Index), new { jobId = vm.JobId });
    }
}
