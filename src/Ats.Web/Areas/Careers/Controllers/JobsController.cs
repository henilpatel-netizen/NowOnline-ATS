using Ats.Application.Abstractions;
using Ats.Application.Career;
using Ats.Web.Areas.Careers.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Areas.Careers.Controllers;

[Area("Careers")]
[AllowAnonymous]
[Route("careers/{slug}")]
public class JobsController : Controller
{
    private static readonly string[] AllowedExtensions = { ".pdf", ".doc", ".docx" };
    private static readonly string[] AllowedContentTypes =
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly ICareerService _career;
    private readonly IFileStore _files;

    public JobsController(ICareerService career, IFileStore files)
    {
        _career = career; _files = files;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string slug)
    {
        ViewData["Title"] = "Open positions";
        ViewData["Slug"] = slug;
        return View(await _career.GetPublishedJobsAsync());
    }

    [HttpGet("jobs/{externalRef}")]
    public async Task<IActionResult> Detail(string slug, string externalRef)
    {
        var job = await _career.GetPublishedJobAsync(externalRef);
        if (job is null) return NotFound();
        var codeParam = await _career.GetCodeParameterNameAsync();
        ViewData["Title"] = job.Title;
        return View(new CareerJobDetailViewModel
        {
            Job = job, Slug = slug, CodeParamName = codeParam,
            Code = Request.Query[codeParam].ToString()
        });
    }

    [HttpPost("jobs/{externalRef}/apply")]
    public async Task<IActionResult> Apply(string slug, string externalRef, CareerApplyFormModel form, IFormFile? resume)
    {
        var job = await _career.GetPublishedJobAsync(externalRef);
        if (job is null) return NotFound();

        async Task<IActionResult> RedisplayAsync(string error)
        {
            var codeParam = await _career.GetCodeParameterNameAsync();
            ViewData["Title"] = job.Title;
            return View("Detail", new CareerJobDetailViewModel
            {
                Job = job, Slug = slug, CodeParamName = codeParam, Code = form.SourceCode,
                FirstName = form.FirstName, LastName = form.LastName, Email = form.Email, Phone = form.Phone,
                Error = error
            });
        }

        if (!ModelState.IsValid) return await RedisplayAsync("Please complete the required fields.");

        var fileError = ValidateResume(resume);
        if (fileError is not null) return await RedisplayAsync(fileError);

        string key;
        await using (var stream = resume!.OpenReadStream())
            key = await _files.SaveAsync(stream, resume.FileName);

        var result = await _career.ApplyAsync(new ApplyInput(
            externalRef, form.FirstName, form.LastName, form.Email, form.Phone, form.SourceCode, key));

        if (!result.Succeeded) return await RedisplayAsync(result.Error ?? "Could not submit your application.");

        return RedirectToAction(nameof(ThankYou), new { slug });
    }

    [HttpGet("thank-you")]
    public IActionResult ThankYou(string slug)
    {
        ViewData["Title"] = "Application received";
        ViewData["Slug"] = slug;
        return View();
    }

    private static string? ValidateResume(IFormFile? resume)
    {
        if (resume is null || resume.Length == 0) return "A resume is required to apply.";
        if (resume.Length > MaxBytes) return "Resume must be 5 MB or smaller.";
        var ext = Path.GetExtension(resume.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return "Resume must be a PDF or Word document (.pdf, .doc, .docx).";
        if (!AllowedContentTypes.Contains(resume.ContentType)) return "Resume file type is not allowed.";
        return null;
    }
}
