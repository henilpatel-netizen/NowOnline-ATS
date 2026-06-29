using Ats.Application.Abstractions;
using Ats.Application.Applications;
using Ats.Application.Candidates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ResumeController : Controller
{
    private readonly IApplicationService _applications;
    private readonly ICandidateService _candidates;
    private readonly IFileStore _files;

    public ResumeController(IApplicationService applications, ICandidateService candidates, IFileStore files)
    {
        _applications = applications; _candidates = candidates; _files = files;
    }

    [HttpGet]
    public async Task<IActionResult> Download(int applicationId)
    {
        var app = await _applications.GetAsync(applicationId);
        if (app is null) return NotFound();
        var candidate = await _candidates.GetAsync(app.CandidateId);
        if (candidate?.ResumeFileKey is null) return NotFound();

        var file = await _files.OpenAsync(candidate.ResumeFileKey);
        if (file is null) return NotFound();

        return File(file.Content, file.ContentType, file.DownloadName);
    }
}
