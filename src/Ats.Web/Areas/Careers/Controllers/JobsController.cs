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

    // Referral attribution: a candidate who arrives on any careers page with the tenant's code
    // query parameter (e.g. ?ref=...) has that code captured into a per-tenant cookie, so it
    // survives navigation from the all-jobs landing page through to the apply form. Without this,
    // the general "all jobs" referral link loses attribution the moment the visitor opens a job.
    private const string RefCookieName = "ats_ref";
    private const int MaxRefLength = 36; // matches ReferralTool's code length limit
    private static readonly TimeSpan RefWindow = TimeSpan.FromDays(30);

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
        ResolveReferralCode(slug, await _career.GetCodeParameterNameAsync()); // capture ?ref on the landing page
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
            Code = ResolveReferralCode(slug, codeParam) ?? string.Empty // query wins, else the captured cookie
        });
    }

    [HttpPost("jobs/{externalRef}/apply")]
    public async Task<IActionResult> Apply(string slug, string externalRef, CareerApplyFormModel form, IFormFile? resume)
    {
        var job = await _career.GetPublishedJobAsync(externalRef);
        if (job is null) return NotFound();

        // Fall back to the captured cookie when the posted form carries no code (e.g. the visitor
        // arrived via the general all-jobs link and the code was never on the job page's URL).
        if (string.IsNullOrWhiteSpace(form.SourceCode))
        {
            var captured = Request.Cookies[RefCookieName];
            if (!string.IsNullOrWhiteSpace(captured)) form.SourceCode = captured;
        }

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

        if (!result.Succeeded)
        {
            await _files.DeleteAsync(key); // don't leak the uploaded file when the application fails
            return await RedisplayAsync(result.Error ?? "Could not submit your application.");
        }

        return RedirectToAction(nameof(ThankYou), new { slug });
    }

    [HttpGet("thank-you")]
    public IActionResult ThankYou(string slug)
    {
        ViewData["Title"] = "Application received";
        ViewData["Slug"] = slug;
        return View();
    }

    // Captures the referral code from the query string (last-touch wins) into a per-tenant cookie
    // and returns the effective code: the query value if present, otherwise the previously
    // captured cookie. The value is treated as untrusted input (trimmed and length-capped) and is
    // only ever forwarded as SourceCode; ReferralTool validates it and rejects unknown codes.
    private string? ResolveReferralCode(string slug, string codeParam)
    {
        var fromQuery = Request.Query[codeParam].ToString();
        if (!string.IsNullOrWhiteSpace(fromQuery))
        {
            var code = fromQuery.Trim();
            if (code.Length > MaxRefLength) code = code[..MaxRefLength];
            Response.Cookies.Append(RefCookieName, code, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,                 // functional, not subject to consent gating
                Path = $"/careers/{slug}",          // scope to this tenant so codes don't bleed across slugs
                Expires = DateTimeOffset.UtcNow.Add(RefWindow)
            });
            return code;
        }

        var fromCookie = Request.Cookies[RefCookieName];
        return string.IsNullOrWhiteSpace(fromCookie) ? null : fromCookie;
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
