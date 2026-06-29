# ATS Phase 2 - Career Site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the public career site (`/careers/{slug}`) where candidates browse a tenant's published jobs, open a job via a `?ref=` link, and apply with a resume; applying captures `Application.SourceCode` and `Candidate.ResumeFileKey`.

**Architecture:** A new `Careers` MVC area with anonymous, slug-routed controllers and its own public layout. A `TenantResolutionMiddleware` maps the slug to a `TenantId` in `HttpContext.Items`, which `HttpTenantContext` reads so the existing global query filter scopes public requests. An `IFileStore` abstraction (local disk impl) stores resumes outside the web root under opaque keys. A `CareerService` performs the apply (create/match candidate, create-or-update application with the referral code).

**Tech Stack:** .NET 10, ASP.NET Core MVC (areas, attribute routing), EF Core 10, Bootstrap 5.

**Reference spec:** `docs/specs/2026-06-29-ats-phase-2-career-site-design.md`. No migration: `ResumeFileKey` and `SourceCode` already exist from Phase 1.

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`; UI tasks add a manual run check.
- **Commits are manual** (developer runs them). The AI pauses and asks.
- **No schema change** is expected. If one is added, the AI creates the migration and the developer applies it.
- **Working directory** is `D:\LiveProject\Ats`.
- **No em dashes and no emoji** in generated files.
- New services/repositories/stores are registered in `Ats.Infrastructure/DependencyInjection.cs`.
- `OperationResult` (from `Ats.Application/Departments/DepartmentService.cs`) and the existing
  `ICandidateRepository` / `IApplicationRepository` are reused.

---

## File structure (created or modified)

```
src\Ats.Application\
  Abstractions\IFileStore.cs                         # NEW
  Career\ICareerRepository.cs, ICareerService.cs, CareerService.cs, CareerModels.cs  # NEW
src\Ats.Domain\Entities\Job.cs                       # MODIFY: Department/Location navs
src\Ats.Infrastructure\
  Files\LocalFileStore.cs                            # NEW
  Persistence\Configurations\JobConfiguration.cs     # MODIFY: nav mapping
  Persistence\Repositories\CareerRepository.cs       # NEW
  Tenancy\HttpTenantContext.cs                       # MODIFY: read Items fallback
  DependencyInjection.cs                             # MODIFY: register IFileStore + career
src\Ats.Web\
  Middleware\TenantResolutionMiddleware.cs           # NEW
  Program.cs                                         # MODIFY: middleware + MapControllers
  appsettings.json                                   # MODIFY: FileStorage:LocalPath
  Controllers\ResumeController.cs                    # NEW (back-office download)
  Views\Applications\Details.cshtml                  # MODIFY: download link
  Areas\Careers\Controllers\JobsController.cs        # NEW
  Areas\Careers\Models\CareerJobDetailViewModel.cs, CareerApplyFormModel.cs  # NEW
  Areas\Careers\Views\_ViewImports.cshtml, _ViewStart.cshtml                 # NEW
  Areas\Careers\Views\Shared\_CareersLayout.cshtml                           # NEW
  Areas\Careers\Views\Jobs\Index.cshtml, Detail.cshtml, ThankYou.cshtml      # NEW
.claude\skills\career-site\SKILL.md                  # NEW
.claude\rules\multi-tenancy.md                       # MODIFY
CLAUDE.md                                            # MODIFY: skill-index
```

---

## Task 1: IFileStore abstraction

**Files:**
- Create: `src/Ats.Application/Abstractions/IFileStore.cs`.

- [ ] **Step 1: Create `IFileStore.cs`**

```csharp
namespace Ats.Application.Abstractions;

public sealed record FileDownload(Stream Content, string ContentType, string DownloadName);

public interface IFileStore
{
    // Stores the content under a generated opaque key (preserving the extension) and returns the key.
    Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default);

    // Opens a previously stored file by key, or null if the key is invalid or missing.
    Task<FileDownload?> OpenAsync(string key, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Ats.Application`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat: add IFileStore abstraction"
```

---

## Task 2: LocalFileStore + config + DI

**Files:**
- Create: `src/Ats.Infrastructure/Files/LocalFileStore.cs`.
- Modify: `src/Ats.Web/appsettings.json`, `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `LocalFileStore.cs`**

```csharp
using Ats.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ats.Infrastructure.Files;

public sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["FileStorage:LocalPath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = "App_Data/uploads";
        _root = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        var key = Guid.NewGuid().ToString("N") + ext;
        var path = Path.Combine(_root, key);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<FileDownload?> OpenAsync(string key, CancellationToken ct = default)
    {
        // Reject anything that is not a bare key (no path separators or traversal).
        if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\') || key.Contains(".."))
            return Task.FromResult<FileDownload?>(null);

        var path = Path.Combine(_root, key);
        if (!File.Exists(path)) return Task.FromResult<FileDownload?>(null);

        Stream stream = File.OpenRead(path);
        var ext = Path.GetExtension(key).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
        return Task.FromResult<FileDownload?>(new FileDownload(stream, contentType, "resume" + ext));
    }
}
```

- [ ] **Step 2: Add the storage path to `src/Ats.Web/appsettings.json`**

Add a `FileStorage` block (sibling of `ConnectionStrings`):

```json
  "FileStorage": {
    "LocalPath": "App_Data/uploads"
  }
```

- [ ] **Step 3: Register `IFileStore` in `DependencyInjection.cs`**

Add `using Ats.Infrastructure.Files;` at the top, and before `return services;`:

```csharp
        services.AddScoped<IFileStore, LocalFileStore>();
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): local file store for resumes"
```

---

## Task 3: Public tenant resolution (HttpTenantContext + middleware + Program)

**Files:**
- Modify: `src/Ats.Infrastructure/Tenancy/HttpTenantContext.cs`.
- Create: `src/Ats.Web/Middleware/TenantResolutionMiddleware.cs`.
- Modify: `src/Ats.Web/Program.cs`.

- [ ] **Step 1: Extend `HttpTenantContext` to read `HttpContext.Items` after the claim**

Replace the `CurrentTenantId` property with:

```csharp
    public int? CurrentTenantId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            var claim = ctx?.User?.FindFirst("tenant_id")?.Value;
            if (int.TryParse(claim, out var id)) return id;

            // Set by TenantResolutionMiddleware for public career-site (slug) requests.
            if (ctx is not null && ctx.Items.TryGetValue("TenantId", out var v) && v is int tid) return tid;

            return null;
        }
    }
```

- [ ] **Step 2: Create `TenantResolutionMiddleware.cs`**

```csharp
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Web.Middleware;

// For public career-site requests (no auth), resolves the {slug} route value to a TenantId and
// stores it in HttpContext.Items so the global query filter scopes the request. Unknown or
// suspended slug returns 404.
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AtsDbContext db)
    {
        var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;
        if (!isAuthenticated && context.GetRouteValue("slug") is string slug && slug.Length > 0)
        {
            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug == slug && t.Status == TenantStatus.Active);
            if (tenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Items["TenantId"] = tenant.Id;
        }

        await _next(context);
    }
}
```

- [ ] **Step 3: Wire the middleware and attribute routing in `Program.cs`**

After `app.UseAuthorization();` add:

```csharp
app.UseMiddleware<Ats.Web.Middleware.TenantResolutionMiddleware>();
```

After the existing `app.MapControllerRoute(...).WithStaticAssets();` add:

```csharp
app.MapControllers();
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (No slug routes exist yet, so behavior is unchanged for the back-office.)

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat: resolve tenant from career-site slug via middleware"
```

---

## Task 4: Job navigations + Career service/repository + DI

**Files:**
- Modify: `src/Ats.Domain/Entities/Job.cs`, `src/Ats.Infrastructure/Persistence/Configurations/JobConfiguration.cs`.
- Create: `src/Ats.Application/Career/CareerModels.cs`, `ICareerRepository.cs`, `ICareerService.cs`, `CareerService.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/CareerRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Add Department/Location navigations to `Job.cs`**

Add these properties to the `Job` class (the FK columns already exist; this adds no schema change):

```csharp
    public Department? Department { get; set; }
    public Location? Location { get; set; }
```

- [ ] **Step 2: Map the navigations in `JobConfiguration.cs`**

Replace the two FK lines for Department and Location with navigation-based mappings:

```csharp
        b.HasOne(j => j.Department).WithMany().HasForeignKey(j => j.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(j => j.Location).WithMany().HasForeignKey(j => j.LocationId).OnDelete(DeleteBehavior.Restrict);
```

(The `PipelineTemplate` FK line is unchanged.)

- [ ] **Step 3: Create `CareerModels.cs`**

```csharp
namespace Ats.Application.Career;

public record ApplyInput(
    string ExternalRef, string FirstName, string LastName, string Email,
    string? Phone, string? SourceCode, string? ResumeFileKey);
```

- [ ] **Step 4: Create `ICareerRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Career;

public interface ICareerRepository
{
    Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default);
    Task<Job?> GetPublishedJobByExternalRefAsync(string externalRef, CancellationToken ct = default);
    Task<string> GetCodeParameterNameAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `ICareerService.cs` and `CareerService.cs`**

```csharp
using Ats.Application.Applications;
using Ats.Application.Candidates;
using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Career;

public interface ICareerService
{
    Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default);
    Task<Job?> GetPublishedJobAsync(string externalRef, CancellationToken ct = default);
    Task<string> GetCodeParameterNameAsync(CancellationToken ct = default);
    Task<OperationResult> ApplyAsync(ApplyInput input, CancellationToken ct = default);
}

public sealed class CareerService : ICareerService
{
    private readonly ICareerRepository _career;
    private readonly ICandidateRepository _candidates;
    private readonly IApplicationRepository _applications;

    public CareerService(ICareerRepository career, ICandidateRepository candidates, IApplicationRepository applications)
    {
        _career = career; _candidates = candidates; _applications = applications;
    }

    public Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default) => _career.GetPublishedJobsAsync(ct);
    public Task<Job?> GetPublishedJobAsync(string externalRef, CancellationToken ct = default) => _career.GetPublishedJobByExternalRefAsync(externalRef, ct);
    public Task<string> GetCodeParameterNameAsync(CancellationToken ct = default) => _career.GetCodeParameterNameAsync(ct);

    public async Task<OperationResult> ApplyAsync(ApplyInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Email)) return OperationResult.Fail("Email is required.");

        var job = await _career.GetPublishedJobByExternalRefAsync(input.ExternalRef, ct);
        if (job is null) return OperationResult.Fail("This job is no longer accepting applications.");

        var stages = await _applications.GetStagesForJobAsync(job.Id, ct);
        var firstStage = stages.OrderBy(s => s.Order).FirstOrDefault();
        if (firstStage is null) return OperationResult.Fail("This job is not accepting applications yet.");

        var email = input.Email.Trim().ToLowerInvariant();
        var candidate = await _candidates.GetByEmailAsync(email, ct);
        if (candidate is null)
        {
            candidate = new Candidate
            {
                FirstName = input.FirstName.Trim(), LastName = input.LastName.Trim(),
                Email = email, Phone = input.Phone?.Trim(), ResumeFileKey = input.ResumeFileKey
            };
            await _candidates.AddAsync(candidate, ct);
            await _candidates.SaveChangesAsync(ct); // assigns candidate.Id
        }
        else
        {
            candidate.FirstName = input.FirstName.Trim();
            candidate.LastName = input.LastName.Trim();
            if (!string.IsNullOrWhiteSpace(input.Phone)) candidate.Phone = input.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(input.ResumeFileKey)) candidate.ResumeFileKey = input.ResumeFileKey;
        }

        var code = string.IsNullOrWhiteSpace(input.SourceCode) ? null : input.SourceCode.Trim();
        if (code is { Length: > 36 }) code = code[..36];

        var existing = await _applications.FindByCandidateJobAsync(candidate.Id, job.Id, ct);
        if (existing is not null)
        {
            if (code is not null) existing.SourceCode = code;   // re-apply: refresh code, no duplicate
            await _candidates.SaveChangesAsync(ct);             // persists candidate + existing changes
            return OperationResult.Ok;
        }

        var application = new JobApplication
        {
            CandidateId = candidate.Id,
            JobId = job.Id,
            CurrentStageId = firstStage.Id,
            SourceCode = code,
            AppliedAt = DateTimeOffset.UtcNow,
            Status = ApplicationStatus.Active
        };
        await _applications.AddApplicationAsync(application, ct);
        await _applications.SaveChangesAsync(ct);

        await _applications.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = null   // public apply, no signed-in user
        }, ct);
        await _applications.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
```

- [ ] **Step 6: Create `CareerRepository.cs`**

```csharp
using Ats.Application.Career;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class CareerRepository : ICareerRepository
{
    private readonly AtsDbContext _db;
    public CareerRepository(AtsDbContext db) => _db = db;

    public Task<List<Job>> GetPublishedJobsAsync(CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.Department).Include(j => j.Location)
            .Where(j => j.Status == JobStatus.Published)
            .OrderByDescending(j => j.PublishedAt)
            .ToListAsync(ct);

    public Task<Job?> GetPublishedJobByExternalRefAsync(string externalRef, CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.Department).Include(j => j.Location)
            .FirstOrDefaultAsync(j => j.ExternalRef == externalRef && j.Status == JobStatus.Published, ct);

    public async Task<string> GetCodeParameterNameAsync(CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(settings?.CodeParameterName) ? "ref" : settings!.CodeParameterName;
    }
}
```

- [ ] **Step 7: Register career services in `DependencyInjection.cs`**

Add `using Ats.Application.Career;` at the top, and before `return services;`:

```csharp
        services.AddScoped<ICareerRepository, CareerRepository>();
        services.AddScoped<ICareerService, CareerService>();
```

- [ ] **Step 8: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit** (developer)

```bash
git add -A
git commit -m "feat: career read service and public apply"
```

---

## Task 5: Careers area (layout, views, controller)

**Files:**
- Create: `src/Ats.Web/Areas/Careers/Views/_ViewImports.cshtml`, `_ViewStart.cshtml`, `Shared/_CareersLayout.cshtml`.
- Create: `src/Ats.Web/Areas/Careers/Models/CareerApplyFormModel.cs`, `CareerJobDetailViewModel.cs`.
- Create: `src/Ats.Web/Areas/Careers/Controllers/JobsController.cs`.
- Create: `src/Ats.Web/Areas/Careers/Views/Jobs/Index.cshtml`, `Detail.cshtml`, `ThankYou.cshtml`.

- [ ] **Step 1: Create `Areas/Careers/Views/_ViewImports.cshtml`**

```cshtml
@using Ats.Web.Areas.Careers.Models
@using Ats.Domain.Entities
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 2: Create `Areas/Careers/Views/_ViewStart.cshtml`**

```cshtml
@{
    Layout = "_CareersLayout";
}
```

- [ ] **Step 3: Create `Areas/Careers/Views/Shared/_CareersLayout.cshtml`**

```cshtml
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - Careers</title>
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/bootstrap-icons/font/bootstrap-icons.min.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
</head>
<body>
    <nav class="navbar bg-white border-bottom mb-4">
        <div class="container"><span class="navbar-brand"><i class="bi bi-briefcase-fill"></i> Careers</span></div>
    </nav>
    <main class="container pb-5">
        @RenderBody()
    </main>
    <script src="~/lib/jquery/dist/jquery.min.js"></script>
    <script src="~/lib/jquery-validation/dist/jquery.validate.min.js"></script>
    <script src="~/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

- [ ] **Step 4: Create `Areas/Careers/Models/CareerApplyFormModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Areas.Careers.Models;

public class CareerApplyFormModel
{
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [StringLength(40)] public string? Phone { get; set; }
    [StringLength(36)] public string? SourceCode { get; set; }
}
```

- [ ] **Step 5: Create `Areas/Careers/Models/CareerJobDetailViewModel.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Web.Areas.Careers.Models;

public class CareerJobDetailViewModel
{
    public Job Job { get; set; } = default!;
    public string Slug { get; set; } = "";
    public string CodeParamName { get; set; } = "ref";
    public string? Code { get; set; }

    // Preserved on a validation re-display.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Error { get; set; }
}
```

- [ ] **Step 6: Create `Areas/Careers/Controllers/JobsController.cs`**

```csharp
using Ats.Application.Career;
using Ats.Application.Abstractions;
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
```

- [ ] **Step 7: Create `Areas/Careers/Views/Jobs/Index.cshtml`** (public board)

```cshtml
@model List<Ats.Domain.Entities.Job>
@{ var slug = ViewData["Slug"] as string; }
<h1 class="h3 mb-4">Open positions</h1>
@if (Model.Count == 0)
{
    <p class="text-muted">There are no open positions right now. Please check back later.</p>
}
<div class="row g-3">
    @foreach (var j in Model)
    {
        <div class="col-md-6">
            <div class="card h-100"><div class="card-body">
                <h2 class="h5 mb-1">
                    <a class="text-decoration-none" asp-controller="Jobs" asp-action="Detail"
                       asp-route-slug="@slug" asp-route-externalRef="@j.ExternalRef">@j.Title</a>
                </h2>
                <div class="text-muted small mb-2">
                    @j.EmploymentType
                    @if (j.Location is not null) { <span>&middot; @(j.Location.City ?? j.Location.Name)</span> }
                    @if (j.Department is not null) { <span>&middot; @j.Department.Name</span> }
                </div>
            </div></div>
        </div>
    }
</div>
```

- [ ] **Step 8: Create `Areas/Careers/Views/Jobs/Detail.cshtml`** (detail + apply form)

```cshtml
@model Ats.Web.Areas.Careers.Models.CareerJobDetailViewModel
<div class="mb-3">
    <a class="btn btn-outline-secondary btn-sm" asp-controller="Jobs" asp-action="Index" asp-route-slug="@Model.Slug">
        <i class="bi bi-arrow-left"></i> All positions
    </a>
</div>
<h1 class="h3">@Model.Job.Title</h1>
<div class="text-muted mb-3">
    @Model.Job.EmploymentType
    @if (Model.Job.Location is not null) { <span>&middot; @(Model.Job.Location.City ?? Model.Job.Location.Name)</span> }
    @if (Model.Job.Department is not null) { <span>&middot; @Model.Job.Department.Name</span> }
</div>
@if (!string.IsNullOrWhiteSpace(Model.Job.Description))
{
    <div class="mb-4" style="white-space:pre-wrap;">@Model.Job.Description</div>
}

<div class="card"><div class="card-body">
    <h2 class="h5 mb-3">Apply for this role</h2>
    @if (!string.IsNullOrEmpty(Model.Error))
    {
        <div class="alert alert-danger">@Model.Error</div>
    }
    <form asp-controller="Jobs" asp-action="Apply" asp-route-slug="@Model.Slug"
          asp-route-externalRef="@Model.Job.ExternalRef" method="post" enctype="multipart/form-data" class="col-lg-8">
        <input type="hidden" name="SourceCode" value="@Model.Code" />
        <div class="row">
            <div class="col-md-6 mb-3">
                <label class="form-label">First name</label>
                <input name="FirstName" value="@Model.FirstName" class="form-control" required />
            </div>
            <div class="col-md-6 mb-3">
                <label class="form-label">Last name</label>
                <input name="LastName" value="@Model.LastName" class="form-control" required />
            </div>
        </div>
        <div class="row">
            <div class="col-md-6 mb-3">
                <label class="form-label">Email</label>
                <input name="Email" type="email" value="@Model.Email" class="form-control" required />
            </div>
            <div class="col-md-6 mb-3">
                <label class="form-label">Phone</label>
                <input name="Phone" value="@Model.Phone" class="form-control" />
            </div>
        </div>
        <div class="mb-3">
            <label class="form-label">Resume (PDF or Word, max 5 MB)</label>
            <input name="resume" type="file" accept=".pdf,.doc,.docx" class="form-control" required />
        </div>
        <button type="submit" class="btn btn-primary">Submit application</button>
    </form>
</div></div>
```

- [ ] **Step 9: Create `Areas/Careers/Views/Jobs/ThankYou.cshtml`**

```cshtml
@{ var slug = ViewData["Slug"] as string; }
<div class="text-center py-5">
    <i class="bi bi-check-circle text-success" style="font-size:2.5rem;"></i>
    <h1 class="h3 mt-3">Application received</h1>
    <p class="text-muted">Thank you for applying. We will be in touch.</p>
    <a class="btn btn-outline-secondary" asp-controller="Jobs" asp-action="Index" asp-route-slug="@slug">Back to open positions</a>
</div>
```

- [ ] **Step 10: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 11: Run and verify the public site**

Run: `dotnet run --project src/Ats.Web`. With a tenant slug that has a published job (for example `acme`),
browse (https) `/careers/acme`. Open a job, append `?ref=1RR123456` to the detail URL, fill the form,
attach a PDF, submit.
Expected: board lists only published jobs; detail shows the job; submit lands on the thank-you page.
Then in the back-office board for that job, confirm a new card in the first stage. Stop the app.

- [ ] **Step 12: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): public career site area (board, detail, apply)"
```

---

## Task 6: Back-office resume download

**Files:**
- Create: `src/Ats.Web/Controllers/ResumeController.cs`.
- Modify: `src/Ats.Web/Views/Applications/Details.cshtml`.

- [ ] **Step 1: Create `ResumeController.cs`**

```csharp
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
```

- [ ] **Step 2: Add a download link on `Views/Applications/Details.cshtml`**

After the `<dl class="row">...</dl>` block, add:

```cshtml
@if (Model.Application.Candidate?.ResumeFileKey is not null)
{
    <a class="btn btn-outline-secondary btn-sm mb-3" asp-controller="Resume" asp-action="Download" asp-route-applicationId="@Model.Application.Id">
        <i class="bi bi-file-earmark-arrow-down"></i> Download resume
    </a>
}
```

> `ApplicationDetailsViewModel.Application` is a `JobApplication` whose `Candidate` navigation is not
> loaded by `IApplicationService.GetAsync`. To make the link condition work, the Details action already
> fetches the candidate name via `ListForJobAsync`; reuse that: in `ApplicationsController.Details`,
> set the loaded application's `Candidate` from that lookup before passing the model. Replace the
> name line in `ApplicationsController.Details` with:

```csharp
        var withCandidate = (await _service.ListForJobAsync(app.JobId)).FirstOrDefault(a => a.Id == id);
        app.Candidate = withCandidate?.Candidate;
        var name = app.Candidate?.FullName ?? "(unknown)";
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run and verify**

Run the app, open an application that was created via the career-site apply (so it has a resume), open
its Details, click Download resume.
Expected: the resume downloads with the correct content type. Stop the app.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): back-office resume download"
```

---

## Task 7: Documentation (career-site skill, rule, index)

**Files:**
- Create: `.claude/skills/career-site/SKILL.md`.
- Modify: `.claude/rules/multi-tenancy.md`, `CLAUDE.md`.

- [ ] **Step 1: Create `.claude/skills/career-site/SKILL.md`**

```markdown
---
name: career-site
description: The Ats public career site - the Careers area, slug-based tenant resolution, IFileStore resume storage, and the public apply flow. Read before changing public-facing or file-upload behavior.
---

# Ats Career Site (Phase 2)

## Area and routes
Public pages live in the `Careers` MVC area (`Areas/Careers`), anonymous, attribute-routed under
`careers/{slug}`: board (`""`), detail (`jobs/{externalRef}`), apply (`POST jobs/{externalRef}/apply`),
thank-you (`thank-you`). The area has its own `_CareersLayout` (no sidebar) reusing `site.css` tokens.

## Tenant resolution
`TenantResolutionMiddleware` (after `UseAuthorization`) reads the `{slug}` route value on unauthenticated
requests, looks up an Active tenant, and sets `HttpContext.Items["TenantId"]`. `HttpTenantContext` reads
the `tenant_id` claim first, then that item. Unknown or suspended slug returns 404. The global query
filter then scopes every public query. This is a documented tenant-resolution source (see
`.claude/rules/multi-tenancy.md`).

## File storage
`IFileStore` (Application) with `LocalFileStore` (Infrastructure) writes to `FileStorage:LocalPath`
(outside `wwwroot`), returns an opaque `{guid}{ext}` key (no original filename, no traversal). Resumes
are never web-served; back-office download streams them via `ResumeController` (`[Authorize]`).

## Apply flow
`ICareerService.ApplyAsync` resolves the published job by `ExternalRef`, create-or-matches the candidate
by email (sets `ResumeFileKey`), and either updates the existing application's `SourceCode` (re-apply, no
duplicate, no stage change) or creates a new application at the first stage with the initial
`ApplicationEvent` (`MovedByUserId` null for public apply). The controller validates the resume
(.pdf/.doc/.docx, content-type, 5 MB) before storing; `IFormFile` never reaches the Application layer.

## Security
Only Published jobs are exposed (404 otherwise); resume validated + stored outside web root; antiforgery
on the apply POST; tenant strictly from the slug with fail-closed filtering.
```

- [ ] **Step 2: Update `.claude/rules/multi-tenancy.md`**

Add a bullet under the resolution notes:

```markdown
- Public career-site requests resolve the tenant from the `{slug}` route value:
  `TenantResolutionMiddleware` sets `HttpContext.Items["TenantId"]` and `HttpTenantContext` reads it
  after the `tenant_id` claim. This is the only place `Items["TenantId"]` is set. Unknown/suspended
  slug returns 404. Querying `Tenants` by slug is unfiltered (Tenant is not an `ITenantEntity`).
```

- [ ] **Step 3: Add the skill-index row to `CLAUDE.md`**

After the Pipeline row:

```markdown
| Career site | `.claude/skills/career-site/SKILL.md` | Careers area, slug tenancy, IFileStore, public apply |
```

- [ ] **Step 4: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "docs: add career-site skill and update tenancy rule + index"
```

---

## Task 8: Manual end-to-end verification of Phase 2

**No new files.**

- [ ] **Step 1: Run the app** (`dotnet run --project src/Ats.Web`, https). Ensure the tenant has at least one Published job (from Phase 1).

- [ ] **Step 2: Public board** - browse `/careers/{slug}`. Expect only Published jobs; a Draft or Closed job does not appear.

- [ ] **Step 3: Unknown slug** - browse `/careers/no-such-tenant`. Expect 404.

- [ ] **Step 4: Detail + code capture** - open a job, append `?ref=1RR123456`. Apply with name/email/phone and a valid PDF. Expect the thank-you page.

- [ ] **Step 5: Back-office check** - sign in, open that job's board. Expect a new card in the first stage. Open its Details. Expect `SourceCode` reflected (current stage Applied) and a working Download resume link.

- [ ] **Step 6: Re-apply** - on the career site, apply again with the same email to the same job but a different `?ref=`. Expect no duplicate card; the application's `SourceCode` updates (verify via SQL or Details after Phase 3 surfaces it; for now confirm no second card).

- [ ] **Step 7: Bad upload** - try applying with a .txt file or a file over 5 MB. Expect a friendly rejection, no application created.

- [ ] **Step 8: Tenancy** - confirm `/careers/{tenantA}` never lists tenant B's jobs, and applying on A creates data only under A.

- [ ] **Step 9: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 2 career site complete and verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage:** `IFileStore` + local impl (Tasks 1, 2); slug tenant resolution via middleware +
  `HttpTenantContext` (Task 3); Careers area board/detail/apply with code capture and resume validation
  (Tasks 4, 5); re-apply update rule and `SourceCode`/`ResumeFileKey` capture (Task 4 `CareerService`);
  back-office resume download (Task 6); security (validation, outside-webroot storage, 404s, antiforgery
  via global filter) across Tasks 2, 5, 6; docs + tenancy-rule update (Task 7); verification incl.
  cross-tenant, unknown slug, bad upload, re-apply (Task 8).
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion. Task 6 Step 2
  includes the exact `ApplicationsController.Details` change needed for the `Candidate` navigation.
- **Type consistency:** `ApplyInput` shape matches `CareerService.ApplyAsync` and the controller call;
  `IFileStore.SaveAsync/OpenAsync` + `FileDownload` match across abstraction, impl, and `ResumeController`;
  `ICareerRepository` members match `CareerRepository` and `CareerService`; `Job.Department`/`Job.Location`
  navs added in Task 4 are used by `CareerRepository` includes and the area views. `OperationResult`,
  `ICandidateRepository`, `IApplicationRepository` reused unchanged from Phase 1.
- **No schema change:** `ResumeFileKey` and `SourceCode` already exist; adding navigation properties maps
  to existing FK columns, so no migration. Confirmed.
- **Ordering:** every task builds green on its own; the area (Task 5) depends only on the services from
  Task 4 and the middleware from Task 3.
```
