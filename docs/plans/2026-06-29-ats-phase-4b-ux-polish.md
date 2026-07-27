# ATS Phase 4 - Plan B: UX Polish (pagination, error pages, UI pass)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the back-office feel finished: paginated/searchable Jobs, Candidates, and delivery-log lists; styled 404/403/500 pages; and a bounded UI/UX polish pass.

**Architecture:** A shared `PagedResult<T>` and a `_Pager` partial back server-side paging added to three list endpoints (with simple search/status filters). Status-code re-execution renders friendly error pages on a neutral layout. The polish pass touches layouts, `site.js`, and the alerts partial only.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, Bootstrap 5.

**Reference spec:** `docs/specs/2026-06-29-ats-phase-4-polish-design.md` (Plan B of two). No migration.

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`; UI tasks add a run check.
- **Commits are manual** (developer). No migration.
- **Working directory** `D:\LiveProject\Ats`. Stop any running app before building.
- **No em dashes and no emoji** in generated files (favicon uses an inline SVG glyph, which is allowed).
- Default page size is 20.

---

## File structure (created or modified)

```
src\Ats.Application\Common\PagedResult.cs                              # NEW
src\Ats.Web\Models\PagerModel.cs, JobsIndexViewModel.cs               # NEW
src\Ats.Web\Views\Shared\_Pager.cshtml                                # NEW
src\Ats.Application\Jobs\IJobRepository.cs, JobService.cs             # MODIFY: SearchAsync
src\Ats.Infrastructure\Persistence\Repositories\JobRepository.cs      # MODIFY
src\Ats.Web\Controllers\JobsController.cs, Views\Jobs\Index.cshtml    # MODIFY
src\Ats.Application\Candidates\ICandidateRepository.cs, CandidateService.cs  # MODIFY
src\Ats.Infrastructure\Persistence\Repositories\CandidateRepository.cs       # MODIFY
src\Ats.Web\Models\CandidatesIndexViewModel.cs, Controllers\CandidatesController.cs, Views\Candidates\Index.cshtml  # MODIFY
src\Ats.Application\Integration\IDeliveryLogService.cs               # MODIFY: paged search
src\Ats.Infrastructure\Integration\DeliveryLogService.cs            # MODIFY
src\Ats.Web\Models\DeliveryLogViewModel.cs                          # NEW
src\Ats.Web\Controllers\IntegrationController.cs, Views\Integration\Deliveries.cshtml  # MODIFY
src\Ats.Web\Controllers\HomeController.cs                           # MODIFY: Status action
src\Ats.Web\Views\Home\Status.cshtml                               # NEW
src\Ats.Web\Views\Shared\Error.cshtml                              # MODIFY: neutral layout
src\Ats.Web\Program.cs                                             # MODIFY: status-code pages
src\Ats.Web\Views\Shared\_Layout.cshtml, _AuthLayout.cshtml, Areas\Careers\Views\Shared\_CareersLayout.cshtml  # MODIFY: favicon
src\Ats.Web\Views\Shared\_Alerts.cshtml                            # MODIFY: dismissible
src\Ats.Web\wwwroot\js\site.js                                     # MODIFY: submit busy-state
.claude\skills\ui\SKILL.md                                         # MODIFY: pager, errors, polish
```

---

## Task 1: PagedResult, PagerModel, and the pager partial

**Files:**
- Create: `src/Ats.Application/Common/PagedResult.cs`, `src/Ats.Web/Models/PagerModel.cs`, `src/Ats.Web/Views/Shared/_Pager.cshtml`.

- [ ] **Step 1: Create `PagedResult.cs`**

```csharp
namespace Ats.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize));
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}
```

- [ ] **Step 2: Create `PagerModel.cs`**

```csharp
namespace Ats.Web.Models;

public class PagerModel
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public string Action { get; set; } = "Index";
    // Extra query values to preserve across pages (e.g. q, status). Non-null only.
    public Dictionary<string, string> Query { get; set; } = new();
}
```

- [ ] **Step 3: Create `Views/Shared/_Pager.cshtml`**

```cshtml
@model Ats.Web.Models.PagerModel
@if (Model.TotalPages > 1)
{
    <nav aria-label="Pagination">
        <ul class="pagination pagination-sm">
            <li class="page-item @(Model.Page <= 1 ? "disabled" : "")">
                <a class="page-link" asp-action="@Model.Action" asp-route-page="@(Model.Page - 1)" asp-all-route-data="Model.Query">Previous</a>
            </li>
            <li class="page-item disabled"><span class="page-link">Page @Model.Page of @Model.TotalPages</span></li>
            <li class="page-item @(Model.Page >= Model.TotalPages ? "disabled" : "")">
                <a class="page-link" asp-action="@Model.Action" asp-route-page="@(Model.Page + 1)" asp-all-route-data="Model.Query">Next</a>
            </li>
        </ul>
    </nav>
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat: PagedResult and pager partial"
```

---

## Task 2: Jobs pagination + search/filter

**Files:**
- Modify: `src/Ats.Application/Jobs/IJobRepository.cs`, `JobService.cs`, `src/Ats.Infrastructure/Persistence/Repositories/JobRepository.cs`.
- Create: `src/Ats.Web/Models/JobsIndexViewModel.cs`.
- Modify: `src/Ats.Web/Controllers/JobsController.cs`, `src/Ats.Web/Views/Jobs/Index.cshtml`.

- [ ] **Step 1: Add `SearchAsync` to `IJobRepository.cs`**

```csharp
    Task<(List<Job> Jobs, int Total)> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
```
Add `using Ats.Domain.Enums;` to the file if not present.

- [ ] **Step 2: Implement it in `JobRepository.cs`**

```csharp
    public async Task<(List<Job> Jobs, int Total)> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Jobs.AsQueryable();
        if (status is not null) query = query.Where(j => j.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(j => j.Title.Contains(s) || j.ExternalRef.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var jobs = await query.OrderByDescending(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (jobs, total);
    }
```
Add `using Ats.Domain.Enums;` to the file if not present.

- [ ] **Step 3: Add `SearchAsync` to `JobService.cs` (interface + impl)**

Interface:

```csharp
    Task<PagedResult<Job>> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
```
Impl (add `using Ats.Application.Common;`):

```csharp
    public async Task<PagedResult<Job>> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var (jobs, total) = await _repo.SearchAsync(status, search, page, pageSize, ct);
        return new PagedResult<Job>(jobs, page, pageSize, total);
    }
```

- [ ] **Step 4: Create `JobsIndexViewModel.cs`**

```csharp
using Ats.Application.Common;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class JobsIndexViewModel
{
    public PagedResult<Job> Results { get; set; } = default!;
    public string? Q { get; set; }
    public JobStatus? Status { get; set; }
}
```

- [ ] **Step 5: Update `JobsController.Index`**

Replace the `Index` action with:

```csharp
    public async Task<IActionResult> Index(string? q, JobStatus? status, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _jobs.SearchAsync(status, q, page, 20);
        return View(new JobsIndexViewModel { Results = results, Q = q, Status = status });
    }
```
Add `using Ats.Domain.Enums;` and `using Ats.Web.Models;` if not present (Models is already used).

- [ ] **Step 6: Replace `Views/Jobs/Index.cshtml`**

```cshtml
@model Ats.Web.Models.JobsIndexViewModel
@using Ats.Domain.Enums
@{ ViewData["Title"] = "Jobs"; }
<div class="d-flex justify-content-between align-items-center mb-3">
    <a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New job</a>
    <form asp-action="Index" method="get" class="d-flex gap-2">
        <input name="q" value="@Model.Q" class="form-control form-control-sm" placeholder="Search title or ref" style="width:14rem" />
        <select name="status" class="form-select form-select-sm" style="width:auto">
            <option value="">All statuses</option>
            @foreach (var st in Enum.GetValues<JobStatus>())
            {
                <option value="@st" selected="@(Model.Status == st)">@st</option>
            }
        </select>
        <button class="btn btn-outline-secondary btn-sm" type="submit">Filter</button>
    </form>
</div>
<table class="table table-hover bg-white">
    <thead><tr><th>Ref</th><th>Title</th><th>Status</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var j in Model.Results.Items)
    {
        <tr>
            <td><code>@j.ExternalRef</code></td>
            <td>@j.Title</td>
            <td>
                @{
                    var badge = j.Status switch {
                        JobStatus.Published => "bg-success",
                        JobStatus.Closed => "bg-secondary",
                        _ => "bg-warning text-dark"
                    };
                }
                <span class="badge @badge">@j.Status</span>
            </td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-primary" asp-controller="Board" asp-action="Index" asp-route-jobId="@j.Id">Board</a>
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@j.Id">Edit</a>
                @if (j.Status != JobStatus.Published)
                {
                    <form asp-action="Publish" asp-route-id="@j.Id" method="post" class="d-inline">
                        <button class="btn btn-sm btn-outline-success" type="submit">Publish</button>
                    </form>
                }
                @if (j.Status == JobStatus.Published)
                {
                    <form asp-action="Close" asp-route-id="@j.Id" method="post" class="d-inline">
                        <button class="btn btn-sm btn-outline-secondary" type="submit">Close</button>
                    </form>
                }
                <form asp-action="Delete" asp-route-id="@j.Id" method="post" class="d-inline"
                      onsubmit="return confirm('Delete this job?');">
                    <button class="btn btn-sm btn-outline-danger" type="submit">Delete</button>
                </form>
            </td>
        </tr>
    }
    @if (Model.Results.Items.Count == 0) { <tr><td colspan="4" class="text-muted">No jobs match.</td></tr> }
    </tbody>
</table>
@{
    var jq = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(Model.Q)) jq["q"] = Model.Q;
    if (Model.Status is not null) jq["status"] = Model.Status.ToString()!;
}
<partial name="_Pager" model="new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Index", Query = jq }" />
```

- [ ] **Step 7: Build and run**

Run: `dotnet build` (0 errors), then run, open `/Jobs`, confirm search by title/ref and the status
filter work and the pager appears past 20 rows. Stop the app.

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): jobs list pagination and search/filter"
```

---

## Task 3: Candidates pagination + search

**Files:**
- Modify: `src/Ats.Application/Candidates/ICandidateRepository.cs`, `CandidateService.cs`, `src/Ats.Infrastructure/Persistence/Repositories/CandidateRepository.cs`.
- Modify: `src/Ats.Web/Models/CandidatesIndexViewModel.cs`, `src/Ats.Web/Controllers/CandidatesController.cs`, `src/Ats.Web/Views/Candidates/Index.cshtml`.

- [ ] **Step 1: Add `SearchAsync` to `ICandidateRepository.cs`**

```csharp
    Task<(List<Candidate> Candidates, int Total)> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `CandidateRepository.cs`**

```csharp
    public async Task<(List<Candidate> Candidates, int Total)> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Candidates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.FirstName.Contains(s) || c.LastName.Contains(s) || c.Email.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var candidates = await query.OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (candidates, total);
    }
```

- [ ] **Step 3: Add `SearchAsync` to `CandidateService.cs` (interface + impl)**

Interface (add `using Ats.Application.Common;`):

```csharp
    Task<PagedResult<Candidate>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default);
```
Impl:

```csharp
    public async Task<PagedResult<Candidate>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var (candidates, total) = await _repo.SearchAsync(search, page, pageSize, ct);
        return new PagedResult<Candidate>(candidates, page, pageSize, total);
    }
```

- [ ] **Step 4: Update `CandidatesIndexViewModel.cs`**

Replace the `Candidates` list with a paged result + query:

```csharp
using Ats.Application.Common;
using Ats.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class CandidatesIndexViewModel
{
    public PagedResult<Candidate> Results { get; set; } = default!;
    public string? Q { get; set; }
    public List<SelectListItem> PublishedJobs { get; set; } = new();
}
```

- [ ] **Step 5: Update `CandidatesController.Index`**

```csharp
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _service.SearchAsync(q, page, 20);
        var jobs = (await _jobs.ListAsync())
            .Where(j => j.Status == JobStatus.Published)
            .Select(j => new SelectListItem($"{j.ExternalRef} - {j.Title}", j.Id.ToString())).ToList();
        return View(new CandidatesIndexViewModel { Results = results, Q = q, PublishedJobs = jobs });
    }
```

- [ ] **Step 6: Replace `Views/Candidates/Index.cshtml`**

```cshtml
@model Ats.Web.Models.CandidatesIndexViewModel
@{ ViewData["Title"] = "Candidates"; }
<div class="d-flex justify-content-between align-items-center mb-3">
    <a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New candidate</a>
    <form asp-action="Index" method="get" class="d-flex gap-2">
        <input name="q" value="@Model.Q" class="form-control form-control-sm" placeholder="Search name or email" style="width:16rem" />
        <button class="btn btn-outline-secondary btn-sm" type="submit">Search</button>
    </form>
</div>
<table class="table table-hover bg-white align-middle">
    <thead><tr><th>Name</th><th>Email</th><th>Phone</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var c in Model.Results.Items)
    {
        <tr>
            <td>@c.FullName</td>
            <td>@c.Email</td>
            <td>@c.Phone</td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@c.Id">Edit</a>
                @if (Model.PublishedJobs.Count > 0)
                {
                    <form asp-action="AddToJob" method="post" class="d-inline-flex gap-1 align-items-center ms-1">
                        <input type="hidden" name="candidateId" value="@c.Id" />
                        <select name="jobId" class="form-select form-select-sm" style="width:auto" asp-items="Model.PublishedJobs">
                            <option value="">Add to job...</option>
                        </select>
                        <button class="btn btn-sm btn-outline-primary" type="submit">Add</button>
                    </form>
                }
            </td>
        </tr>
    }
    @if (Model.Results.Items.Count == 0) { <tr><td colspan="4" class="text-muted">No candidates match.</td></tr> }
    </tbody>
</table>
@{
    var cq = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(Model.Q)) cq["q"] = Model.Q;
}
<partial name="_Pager" model="new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Index", Query = cq }" />
@if (Model.PublishedJobs.Count == 0)
{
    <p class="text-muted small">Publish a job to enable "Add to job" here.</p>
}
```

- [ ] **Step 7: Build and run**

Run: `dotnet build` (0 errors), then run, open `/Candidates`, confirm search and pager work. Stop the app.

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): candidates list pagination and search"
```

---

## Task 4: Delivery log pagination + status filter

**Files:**
- Modify: `src/Ats.Application/Integration/IDeliveryLogService.cs`, `src/Ats.Infrastructure/Integration/DeliveryLogService.cs`.
- Create: `src/Ats.Web/Models/DeliveryLogViewModel.cs`.
- Modify: `src/Ats.Web/Controllers/IntegrationController.cs`, `src/Ats.Web/Views/Integration/Deliveries.cshtml`.

- [ ] **Step 1: Replace `IDeliveryLogService.cs`**

```csharp
using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Integration;

public interface IDeliveryLogService
{
    Task<PagedResult<DeliveryLogEntry>> SearchAsync(OutboxStatus? status, int page, int pageSize, CancellationToken ct = default);
}
```

- [ ] **Step 2: Replace `DeliveryLogService.cs`**

```csharp
using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class DeliveryLogService : IDeliveryLogService
{
    private readonly AtsDbContext _db;
    public DeliveryLogService(AtsDbContext db) => _db = db;

    public async Task<PagedResult<DeliveryLogEntry>> SearchAsync(OutboxStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.OutboxMessages.AsQueryable();
        if (status is not null) query = query.Where(m => m.Status == status);

        var total = await query.CountAsync(ct);
        var messages = await query.OrderByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();
        var deliveries = await _db.WebhookDeliveries
            .Where(d => ids.Contains(d.OutboxMessageId))
            .ToListAsync(ct);

        var items = messages
            .Select(m => new DeliveryLogEntry(
                m, deliveries.Where(d => d.OutboxMessageId == m.Id).OrderBy(d => d.Id).ToList()))
            .ToList();

        return new PagedResult<DeliveryLogEntry>(items, page, pageSize, total);
    }
}
```

- [ ] **Step 3: Create `DeliveryLogViewModel.cs`**

```csharp
using Ats.Application.Common;
using Ats.Application.Integration;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class DeliveryLogViewModel
{
    public PagedResult<DeliveryLogEntry> Results { get; set; } = default!;
    public OutboxStatus? Status { get; set; }
}
```

- [ ] **Step 4: Update `IntegrationController.Deliveries`**

```csharp
    [HttpGet]
    public async Task<IActionResult> Deliveries(OutboxStatus? status, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _log.SearchAsync(status, page, 20);
        return View(new DeliveryLogViewModel { Results = results, Status = status });
    }
```
Add `using Ats.Domain.Enums;` to the controller if not present.

- [ ] **Step 5: Replace `Views/Integration/Deliveries.cshtml`**

```cshtml
@model Ats.Web.Models.DeliveryLogViewModel
@using Ats.Domain.Enums
@{ ViewData["Title"] = "Delivery log"; }
<div class="d-flex justify-content-between align-items-center mb-3">
    <a class="btn btn-outline-secondary btn-sm" asp-action="Index"><i class="bi bi-arrow-left"></i> Integration</a>
    <form asp-action="Deliveries" method="get" class="d-flex gap-2">
        <select name="status" class="form-select form-select-sm" style="width:auto">
            <option value="">All states</option>
            @foreach (var st in Enum.GetValues<OutboxStatus>())
            {
                <option value="@st" selected="@(Model.Status == st)">@st</option>
            }
        </select>
        <button class="btn btn-outline-secondary btn-sm" type="submit">Filter</button>
    </form>
</div>
<table class="table bg-white align-middle">
    <thead><tr><th>#</th><th>Candidate ext id</th><th>Vacancy</th><th>Status sent</th><th>State</th><th>Attempts</th><th>Last attempt</th></tr></thead>
    <tbody>
    @foreach (var e in Model.Results.Items)
    {
        var last = e.Deliveries.LastOrDefault();
        var badge = e.Message.Status switch
        {
            OutboxStatus.Delivered => "bg-success",
            OutboxStatus.Failed => "bg-danger",
            _ => "bg-warning text-dark"
        };
        <tr>
            <td>@e.Message.Id</td>
            <td><code>@e.Message.ExternalCandidateId</code></td>
            <td><code>@e.Message.ExternalVacancyId</code></td>
            <td>@e.Message.CandidateStatus</td>
            <td><span class="badge @badge">@e.Message.Status</span></td>
            <td>@e.Message.Attempts</td>
            <td>
                @if (last is not null)
                {
                    <span class="small text-muted">@last.Kind @(last.HttpStatus?.ToString() ?? "-") at @last.AttemptedAt.ToLocalTime().ToString("g")</span>
                }
                @if (!string.IsNullOrEmpty(e.Message.LastError))
                {
                    <div class="small text-danger">@e.Message.LastError</div>
                }
            </td>
        </tr>
    }
    @if (Model.Results.Items.Count == 0) { <tr><td colspan="7" class="text-muted">No status updates match.</td></tr> }
    </tbody>
</table>
@{
    var dq = new Dictionary<string, string>();
    if (Model.Status is not null) dq["status"] = Model.Status.ToString()!;
}
<partial name="_Pager" model="new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Deliveries", Query = dq }" />
```

- [ ] **Step 6: Build and run**

Run: `dotnet build` (0 errors), then run, open the delivery log, confirm the state filter and pager work.
Stop the app.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): delivery log pagination and status filter"
```

---

## Task 5: Custom error pages

**Files:**
- Modify: `src/Ats.Web/Program.cs`, `src/Ats.Web/Controllers/HomeController.cs`, `src/Ats.Web/Views/Shared/Error.cshtml`.
- Create: `src/Ats.Web/Views/Home/Status.cshtml`.

- [ ] **Step 1: Add status-code re-execution in `Program.cs`**

Immediately after the `if (!app.Environment.IsDevelopment()) { ... }` block (where `UseExceptionHandler`
is configured) and before `app.UseHttpsRedirection();`, add:

```csharp
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
```

> Note: the `UseExceptionHandler("/Home/Error")` line stays inside the non-Development block from the
> template. Status-code pages run in all environments.

- [ ] **Step 2: Add the `Status` action to `HomeController.cs`**

Add this action:

```csharp
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        ViewData["Code"] = code;
        ViewData["Message"] = code switch
        {
            404 => "We could not find that page.",
            403 => "You do not have access to that.",
            _ => "Something went wrong."
        };
        Response.StatusCode = code;
        return View();
    }
```

- [ ] **Step 3: Create `Views/Home/Status.cshtml`** (neutral centered layout works for back-office and careers)

```cshtml
@{
    Layout = "_AuthLayout";
    var code = ViewData["Code"];
    ViewData["Title"] = $"Error {code}";
}
<div class="text-center">
    <div class="display-5 fw-semibold">@code</div>
    <p class="text-muted">@ViewData["Message"]</p>
    <a class="btn btn-outline-secondary btn-sm" href="/">Go home</a>
</div>
```

- [ ] **Step 4: Replace `Views/Shared/Error.cshtml`** (500 page on the neutral layout)

```cshtml
@{
    Layout = "_AuthLayout";
    ViewData["Title"] = "Error";
}
<div class="text-center">
    <div class="display-5 fw-semibold">Something went wrong</div>
    <p class="text-muted">An unexpected error occurred. Please try again.</p>
    <a class="btn btn-outline-secondary btn-sm" href="/">Go home</a>
</div>
```

> The default template `Error.cshtml` referenced `ErrorViewModel`/`RequestId`; this neutral page does not
> need a model. `HomeController.Error` may still pass a model; that is harmless since the view ignores it.

- [ ] **Step 5: Build and run**

Run: `dotnet build` (0 errors), then run. Browse a bad back-office URL (for example `/Jobs/Nope/123`) and
confirm the styled 404; sign in as a non-Owner and hit `/Integration` to confirm the 403; browse
`/careers/unknown-slug` and confirm the same neutral 404. Stop the app.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): styled 404/403/500 error pages"
```

---

## Task 6: UI/UX polish pass

**Files:**
- Modify: `src/Ats.Web/Views/Shared/_Layout.cshtml`, `_AuthLayout.cshtml`, `src/Ats.Web/Areas/Careers/Views/Shared/_CareersLayout.cshtml` (favicon).
- Modify: `src/Ats.Web/Views/Shared/_Alerts.cshtml` (dismissible).
- Modify: `src/Ats.Web/wwwroot/js/site.js` (submit busy-state).

- [ ] **Step 1: Add a favicon link to all three layouts**

In each layout's `<head>` (after the title), add this inline-SVG briefcase favicon (no binary file):

```html
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E%3Crect width='16' height='16' rx='3' fill='%234f46e5'/%3E%3Cpath d='M5 5V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1h2v7H3V5h2zm1 0h4V4H6v1z' fill='white'/%3E%3C/svg%3E" />
```

- [ ] **Step 2: Make `_Alerts.cshtml` dismissible**

```cshtml
@{
    var success = TempData["Success"] as string;
    var error = TempData["Error"] as string;
    var info = TempData["Info"] as string;
}
@if (!string.IsNullOrEmpty(success)) { <div class="alert alert-success alert-dismissible fade show" role="alert">@success<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button></div> }
@if (!string.IsNullOrEmpty(error)) { <div class="alert alert-danger alert-dismissible fade show" role="alert">@error<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button></div> }
@if (!string.IsNullOrEmpty(info)) { <div class="alert alert-info alert-dismissible fade show" role="alert">@info<button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button></div> }
```

- [ ] **Step 3: Add a submit busy-state to `wwwroot/js/site.js`**

Append:

```javascript
// Disable submit buttons on form submit to prevent double-posts and signal progress.
document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!(form instanceof HTMLFormElement)) return;
    var btn = form.querySelector('button[type="submit"], input[type="submit"]');
    if (btn && !btn.disabled) {
        // Let the form post first, then disable on the next tick.
        setTimeout(function () { btn.disabled = true; btn.classList.add('disabled'); }, 0);
    }
}, true);
```

> `_AuthLayout` and `_CareersLayout` do not include `site.js`; the busy-state applies to back-office
> (`_Layout`) forms, which is where double-post risk matters. The auth/apply forms already rely on
> validation. (No change needed there.)

- [ ] **Step 4: Build and run**

Run: `dotnet build` (0 errors), then run. Confirm the favicon shows in the browser tab, flash alerts have
a working close button, and submitting a back-office form disables its button. Stop the app.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): favicon, dismissible alerts, submit busy-state"
```

---

## Task 7: UI skill refresh

**Files:**
- Modify: `.claude/skills/ui/SKILL.md`.

- [ ] **Step 1: Append the polish conventions to `.claude/skills/ui/SKILL.md`**

Add a section:

```markdown
## Lists, pagination, and errors (Phase 4)
- Paginated lists use `PagedResult<T>` (`Ats.Application/Common`) + the `_Pager.cshtml` partial
  (`PagerModel` with `Page`, `TotalPages`, `Action`, and a `Query` dictionary of filters to preserve).
  Jobs, Candidates, and the delivery log follow this with a GET search/filter form (page size 20).
- Error pages: `app.UseStatusCodePagesWithReExecute("/Home/Status/{0}")` renders `HomeController.Status`
  (`Views/Home/Status.cshtml`, neutral `_AuthLayout`) for 404/403; `UseExceptionHandler` renders
  `Views/Shared/Error.cshtml` for 500. The neutral layout serves both back-office and careers visitors.
- Polish: an inline-SVG favicon in all layouts; `_Alerts` are dismissible; `site.js` disables a form's
  submit button on submit (back-office only).
```

- [ ] **Step 2: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "docs: ui skill - pagination, error pages, polish"
```

---

## Task 8: Manual verification (Phase 4 complete)

**No new files.** Run in your session.

- [ ] **Step 1: Jobs/Candidates** - confirm search and filters narrow results, the pager works at
  boundaries, and existing actions (board/edit/publish/close/delete, add-to-job) still work.
- [ ] **Step 2: Delivery log** - confirm the state filter and pager work.
- [ ] **Step 3: Errors** - styled 404 on a bad back-office URL, 403 for a non-Owner on an Owner page,
  and the neutral 404 on an unknown careers slug.
- [ ] **Step 4: Polish** - favicon in the tab, dismissible flash alerts, submit button disables on submit.
- [ ] **Step 5: Tenancy** - confirm searches/lists only ever show the current tenant's rows.
- [ ] **Step 6: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 4 Plan B UX polish complete and verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage (Plan B):** `PagedResult<T>` + pager partial (Task 1); Jobs pagination + status/search
  (Task 2); Candidates pagination + search (Task 3); delivery-log pagination + status filter (Task 4);
  styled 404/403/500 incl. careers via the neutral layout (Task 5); UI/UX polish - favicon, dismissible
  alerts, submit busy-state (Task 6); ui skill refresh (Task 7); verification incl. tenancy (Task 8). No
  migration.
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion. The favicon is an
  inline SVG data URI (no binary asset needed).
- **Type consistency:** `PagedResult<T>` used by `JobService.SearchAsync`, `CandidateService.SearchAsync`,
  and `IDeliveryLogService.SearchAsync`; repo tuple returns `(List<T>, int Total)` match their service
  wrappers; `JobsIndexViewModel`/`CandidatesIndexViewModel`/`DeliveryLogViewModel` expose `Results` +
  filter fields used by their views; `_Pager` consumes `PagerModel` with a `Dictionary<string,string>`
  bound via `asp-all-route-data`; `JobStatus?`/`OutboxStatus?` filter params bind from the query string.
- **Behavior preserved:** the Jobs and Candidates views keep all prior actions (board/edit/lifecycle/
  add-to-job); `IDeliveryLogService.RecentAsync` is replaced by `SearchAsync` and the only caller
  (`IntegrationController.Deliveries`) is updated.
- **Ordering:** every task builds green on its own; the list views depend on Task 1's `PagedResult`/pager.
```
