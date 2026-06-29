# ATS Phase 4 - Plan A: Audit, Dashboards, Test-Feed

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Owner-visible audit log of back-office actions, real dashboard metrics, and a feed preview + ReferralTool connection test on the Integration page.

**Architecture:** An `IAuditLogger` (called from controllers after successful mutations) writes `AuditEntry` rows stamped with the current user. A `DashboardService` aggregates tenant-scoped counts. The Integration page gains a published-jobs preview and a read-only `checkvacancyexists` probe via the existing `IReferralToolClient`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, Bootstrap 5.

**Reference spec:** `docs/specs/2026-06-29-ats-phase-4-polish-design.md` (Plan A of two). One migration: `AddAuditEntry`.

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`.
- **Commits are manual** (developer). Migration created by the AI, applied by the developer.
- **Working directory** `D:\LiveProject\Ats`. Stop any running app before building.
- **No em dashes and no emoji** in generated files.
- Services register in `Ats.Infrastructure/DependencyInjection.cs`.

---

## File structure (created or modified)

```
src\Ats.Domain\Entities\AuditEntry.cs                                  # NEW
src\Ats.Application\Abstractions\ICurrentUser.cs                       # MODIFY: add Name
src\Ats.Infrastructure\Identity\CurrentUser.cs                        # MODIFY: Name
src\Ats.Application\Auditing\IAuditLogger.cs                          # NEW
src\Ats.Infrastructure\Auditing\AuditLogger.cs                       # NEW
src\Ats.Infrastructure\Persistence\Configurations\AuditEntryConfiguration.cs  # NEW
src\Ats.Infrastructure\Persistence\AtsDbContext.cs                   # MODIFY: DbSet
src\Ats.Infrastructure\Migrations\*_AddAuditEntry.cs                  # NEW (generated)
src\Ats.Infrastructure\DependencyInjection.cs                        # MODIFY: register audit, dashboard, RT client
src\Ats.Web\Controllers\*Controller.cs                               # MODIFY: audit calls (Jobs, Pipelines, Departments, Locations, Candidates, Integration)
src\Ats.Web\Controllers\AuditController.cs                           # NEW
src\Ats.Web\Views\Audit\Index.cshtml                                 # NEW
src\Ats.Web\ViewComponents\SidebarNavViewComponent.cs                # MODIFY: Audit entry
src\Ats.Application\Dashboard\DashboardSummary.cs, IDashboardService.cs  # NEW
src\Ats.Infrastructure\Dashboard\DashboardService.cs                 # NEW
src\Ats.Web\Controllers\DashboardController.cs                       # MODIFY
src\Ats.Web\Views\Dashboard\Index.cshtml                            # MODIFY
src\Ats.Web\Models\IntegrationSettingsViewModel.cs                   # MODIFY: PublishedJobCount
src\Ats.Web\Views\Integration\Index.cshtml                          # MODIFY: preview + test button
src\Ats.Worker\Program.cs                                            # MODIFY: drop duplicate HttpClient reg
.claude\skills\audit\SKILL.md                                        # NEW
CLAUDE.md                                                            # MODIFY: skill-index
```

---

## Task 1: AuditEntry, IAuditLogger, current-user name

**Files:**
- Create: `src/Ats.Domain/Entities/AuditEntry.cs`.
- Modify: `src/Ats.Application/Abstractions/ICurrentUser.cs`, `src/Ats.Infrastructure/Identity/CurrentUser.cs`.
- Create: `src/Ats.Application/Auditing/IAuditLogger.cs`, `src/Ats.Infrastructure/Auditing/AuditLogger.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Configurations/AuditEntryConfiguration.cs`.
- Modify: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs`, `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `AuditEntry.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class AuditEntry : TenantEntity
{
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityRef { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}
```

- [ ] **Step 2: Add `Name` to `ICurrentUser.cs`**

Add to the interface:

```csharp
    string? Name { get; }
```

- [ ] **Step 3: Implement `Name` in `CurrentUser.cs`**

Add the property (alongside the existing ones):

```csharp
    public string? Name => _accessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
```

- [ ] **Step 4: Create `IAuditLogger.cs`**

```csharp
namespace Ats.Application.Auditing;

public interface IAuditLogger
{
    Task LogAsync(string action, string entityType, string? entityRef, string summary, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `AuditLogger.cs`**

```csharp
using Ats.Application.Abstractions;
using Ats.Application.Auditing;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Ats.Infrastructure.Auditing;

public sealed class AuditLogger : IAuditLogger
{
    private readonly AtsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(AtsDbContext db, ICurrentUser user, ILogger<AuditLogger> logger)
    {
        _db = db; _user = user; _logger = logger;
    }

    public async Task LogAsync(string action, string entityType, string? entityRef, string summary, CancellationToken ct = default)
    {
        try
        {
            _db.AuditEntries.Add(new AuditEntry
            {
                Action = action,
                EntityType = entityType,
                EntityRef = entityRef,
                Summary = summary,
                UserId = _user.UserId,
                UserName = _user.Name ?? "Unknown",
                OccurredAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditing must never break the action it records.
            _logger.LogError(ex, "Failed to write audit entry for {Action}", action);
        }
    }
}
```

- [ ] **Step 6: Create `AuditEntryConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Action).IsRequired().HasMaxLength(80);
        b.Property(a => a.EntityType).IsRequired().HasMaxLength(80);
        b.Property(a => a.EntityRef).HasMaxLength(80);
        b.Property(a => a.Summary).IsRequired().HasMaxLength(400);
        b.Property(a => a.UserName).IsRequired().HasMaxLength(200);
        b.HasIndex(a => new { a.TenantId, a.OccurredAt });
    }
}
```

- [ ] **Step 7: Add the DbSet in `AtsDbContext`**

```csharp
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
```

- [ ] **Step 8: Register the logger in `DependencyInjection.cs`**

Add `using Ats.Application.Auditing;` and `using Ats.Infrastructure.Auditing;` at the top, and before
`return services;`:

```csharp
        services.AddScoped<IAuditLogger, AuditLogger>();
```

- [ ] **Step 9: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit** (developer)

```bash
git add -A
git commit -m "feat: audit entry entity and audit logger"
```

---

## Task 2: Migration

**Files:**
- Create: `src/Ats.Infrastructure/Migrations/*_AddAuditEntry.cs`.

- [ ] **Step 1: Create the migration**

```bash
cd /d/LiveProject/Ats
dotnet ef migrations add AddAuditEntry --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```
Expected: a migration creating the `AuditEntries` table.

- [ ] **Step 2: Build**, then developer applies:

```bash
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): migration for audit entries"
```

---

## Task 3: Wire audit calls into back-office controllers

Inject `IAuditLogger _audit` into each controller (add the constructor parameter + field +
`using Ats.Application.Auditing;`) and call it after each successful mutation, before the redirect. The
exact calls per controller:

- [ ] **Step 1: `JobsController`** - add field/ctor param. In `Create` (after `TempData["Success"]`):
  `await _audit.LogAsync("JobCreated", "Job", null, $"Created job '{vm.Title}'");`
  In `Edit`: `await _audit.LogAsync("JobUpdated", "Job", vm.Id.ToString(), $"Updated job '{vm.Title}'");`
  In `Publish`/`Close`/`Delete` (inside the success branch, where `result.Succeeded`):
  `await _audit.LogAsync("JobPublished"|"JobClosed"|"JobDeleted", "Job", id.ToString(), $"... job {id}");`
  For `Publish`/`Close` the helper `Lifecycle(...)` runs both; add the audit call in each action before
  calling `Lifecycle`, or change `Lifecycle` to accept an audit action. Simplest: in `Publish`/`Close`
  inline the result handling and audit. Replace the two one-line lifecycle methods with:

```csharp
    [HttpPost]
    public async Task<IActionResult> Publish(int id)
    {
        var result = await _jobs.PublishAsync(id);
        if (result.Succeeded) await _audit.LogAsync("JobPublished", "Job", id.ToString(), $"Published job {id}");
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Job published." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Close(int id)
    {
        var result = await _jobs.CloseAsync(id);
        if (result.Succeeded) await _audit.LogAsync("JobClosed", "Job", id.ToString(), $"Closed job {id}");
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Job closed." : result.Error;
        return RedirectToAction(nameof(Index));
    }
```
  and delete the now-unused `Lifecycle` helper. In `Delete` add (in the success branch):
  `await _audit.LogAsync("JobDeleted", "Job", id.ToString(), $"Deleted job {id}");`

- [ ] **Step 2: `PipelinesController`** - field/ctor. In `Save` (after success): 
  `await _audit.LogAsync("PipelineSaved", "PipelineTemplate", vm.Id?.ToString(), $"Saved pipeline '{vm.Name}'");`
  In `Delete` (success branch): `await _audit.LogAsync("PipelineDeleted", "PipelineTemplate", id.ToString(), $"Deleted pipeline {id}");`

- [ ] **Step 3: `DepartmentsController`** - field/ctor. In `Create`/`Edit`/`Delete` success branches:
  `await _audit.LogAsync("DepartmentCreated", "Department", null, $"Created department '{vm.Name}'");`
  `await _audit.LogAsync("DepartmentUpdated", "Department", vm.Id.ToString(), $"Updated department '{vm.Name}'");`
  `await _audit.LogAsync("DepartmentDeleted", "Department", id.ToString(), $"Deleted department {id}");`

- [ ] **Step 4: `LocationsController`** - field/ctor. Same pattern with `"Location"`:
  `LocationCreated`/`LocationUpdated`/`LocationDeleted`.

- [ ] **Step 5: `CandidatesController`** - field/ctor. In `Create`/`Edit` success branches:
  `await _audit.LogAsync("CandidateCreated", "Candidate", null, $"Created candidate '{vm.FirstName} {vm.LastName}'");`
  `await _audit.LogAsync("CandidateUpdated", "Candidate", vm.Id.ToString(), $"Updated candidate '{vm.FirstName} {vm.LastName}'");`

- [ ] **Step 6: `IntegrationController`** - field/ctor. In `Index` POST (after `UpdateAsync`):
  `await _audit.LogAsync("IntegrationSettingsSaved", "TenantSettings", null, "Updated integration settings");`
  In `GenerateFeedKey` (after generation): `await _audit.LogAsync("FeedKeyRegenerated", "TenantSettings", null, "Regenerated feed API key");`
  (Never include the key in the summary.)

- [ ] **Step 7: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): record back-office actions in the audit log"
```

---

## Task 4: Audit view + sidebar

**Files:**
- Create: `src/Ats.Web/Controllers/AuditController.cs`, `src/Ats.Web/Views/Audit/Index.cshtml`.
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`.
- Create: `src/Ats.Application/Auditing/IAuditQuery.cs`, `src/Ats.Infrastructure/Auditing/AuditQuery.cs`; Modify DI.

- [ ] **Step 1: Create `IAuditQuery.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Auditing;

public interface IAuditQuery
{
    Task<List<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `AuditQuery.cs`**

```csharp
using Ats.Application.Auditing;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Auditing;

public sealed class AuditQuery : IAuditQuery
{
    private readonly AtsDbContext _db;
    public AuditQuery(AtsDbContext db) => _db = db;

    public Task<List<AuditEntry>> RecentAsync(int take = 200, CancellationToken ct = default) =>
        _db.AuditEntries.OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
}
```

- [ ] **Step 3: Register in `DependencyInjection.cs`**

```csharp
        services.AddScoped<IAuditQuery, AuditQuery>();
```

- [ ] **Step 4: Create `AuditController.cs`**

```csharp
using Ats.Application.Auditing;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Roles = AtsRole.Owner)]
public class AuditController : Controller
{
    private readonly IAuditQuery _audit;
    public AuditController(IAuditQuery audit) => _audit = audit;

    public async Task<IActionResult> Index() => View(await _audit.RecentAsync());
}
```

- [ ] **Step 5: Create `Views/Audit/Index.cshtml`**

```cshtml
@model List<Ats.Domain.Entities.AuditEntry>
@{ ViewData["Title"] = "Audit log"; }
<table class="table bg-white align-middle">
    <thead><tr><th>When</th><th>User</th><th>Action</th><th>Entity</th><th>Summary</th></tr></thead>
    <tbody>
    @foreach (var a in Model)
    {
        <tr>
            <td class="small text-muted">@a.OccurredAt.ToLocalTime().ToString("g")</td>
            <td>@a.UserName</td>
            <td><span class="badge bg-secondary">@a.Action</span></td>
            <td>@a.EntityType @(string.IsNullOrEmpty(a.EntityRef) ? "" : $"({a.EntityRef})")</td>
            <td>@a.Summary</td>
        </tr>
    }
    @if (Model.Count == 0) { <tr><td colspan="5" class="text-muted">No activity recorded yet.</td></tr> }
    </tbody>
</table>
<p class="text-muted small">Stage-move history is on each application's Details page; outbound delivery attempts are in the Integration delivery log.</p>
```

- [ ] **Step 6: Add the Audit sidebar entry (Owner-only)**

In `SidebarNavViewComponent.cs`, add to the `Items` array (after Integration):

```csharp
        new("Audit", "bi-journal-text", "Audit", "Index", AtsRole.Owner),
```

- [ ] **Step 7: Build and run**

Run: `dotnet build` (0 errors), then `dotnet run --project src/Ats.Web`, sign in as Owner. Perform an
action (publish a job), open `/Audit`, confirm the entry appears. Confirm a non-Owner cannot see the
entry or reach `/Audit`. Stop the app.

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): owner audit log view"
```

---

## Task 5: Dashboard metrics

**Files:**
- Create: `src/Ats.Application/Dashboard/DashboardSummary.cs`, `IDashboardService.cs`.
- Create: `src/Ats.Infrastructure/Dashboard/DashboardService.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`, `src/Ats.Web/Controllers/DashboardController.cs`, `src/Ats.Web/Views/Dashboard/Index.cshtml`.

- [ ] **Step 1: Create `DashboardSummary.cs`**

```csharp
namespace Ats.Application.Dashboard;

public sealed record StageCount(string Stage, int Count);
public sealed record RecentApplication(string Candidate, string Job, DateTimeOffset AppliedAt);
public sealed record DashboardSummary(
    int PublishedJobs,
    int TotalCandidates,
    int ActiveApplications,
    IReadOnlyList<StageCount> ByStage,
    IReadOnlyList<RecentApplication> Recent);
```

- [ ] **Step 2: Create `IDashboardService.cs`**

```csharp
namespace Ats.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummary> GetAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `DashboardService.cs`**

```csharp
using Ats.Application.Dashboard;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private readonly AtsDbContext _db;
    public DashboardService(AtsDbContext db) => _db = db;

    public async Task<DashboardSummary> GetAsync(CancellationToken ct = default)
    {
        var publishedJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);
        var totalCandidates = await _db.Candidates.CountAsync(ct);
        var activeApplications = await _db.Applications.CountAsync(a => a.Status == ApplicationStatus.Active, ct);

        var grouped = await _db.Applications
            .Where(a => a.Status == ApplicationStatus.Active)
            .GroupBy(a => a.CurrentStageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var stageIds = grouped.Select(g => g.StageId).ToList();
        var stageNames = await _db.PipelineStages
            .Where(s => stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var byStage = grouped
            .Select(g => new StageCount(stageNames.TryGetValue(g.StageId, out var n) ? n : $"#{g.StageId}", g.Count))
            .OrderByDescending(s => s.Count)
            .ToList();

        var recentApps = await _db.Applications
            .Include(a => a.Candidate)
            .OrderByDescending(a => a.AppliedAt)
            .Take(5)
            .ToListAsync(ct);
        var jobIds = recentApps.Select(a => a.JobId).Distinct().ToList();
        var jobTitles = await _db.Jobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, ct);
        var recent = recentApps
            .Select(a => new RecentApplication(
                a.Candidate?.FullName ?? "(unknown)",
                jobTitles.TryGetValue(a.JobId, out var t) ? t : "(job)",
                a.AppliedAt))
            .ToList();

        return new DashboardSummary(publishedJobs, totalCandidates, activeApplications, byStage, recent);
    }
}
```

- [ ] **Step 4: Register in `DependencyInjection.cs`**

Add `using Ats.Application.Dashboard;` and `using Ats.Infrastructure.Dashboard;`, then before
`return services;`:

```csharp
        services.AddScoped<IDashboardService, DashboardService>();
```

- [ ] **Step 5: Replace `DashboardController.cs`**

```csharp
using Ats.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    public async Task<IActionResult> Index() => View(await _dashboard.GetAsync());
}
```

- [ ] **Step 6: Replace `Views/Dashboard/Index.cshtml`**

```cshtml
@model Ats.Application.Dashboard.DashboardSummary
@{ ViewData["Title"] = "Dashboard"; }
<div class="row g-3 mb-4">
    <div class="col-sm-4"><div class="card h-100"><div class="card-body">
        <div class="text-muted small">Published jobs</div><div class="fs-3 fw-semibold">@Model.PublishedJobs</div>
    </div></div></div>
    <div class="col-sm-4"><div class="card h-100"><div class="card-body">
        <div class="text-muted small">Candidates</div><div class="fs-3 fw-semibold">@Model.TotalCandidates</div>
    </div></div></div>
    <div class="col-sm-4"><div class="card h-100"><div class="card-body">
        <div class="text-muted small">Active applications</div><div class="fs-3 fw-semibold">@Model.ActiveApplications</div>
    </div></div></div>
</div>

<div class="row g-3">
    <div class="col-md-6">
        <div class="card h-100"><div class="card-body">
            <h2 class="h6">Active applications by stage</h2>
            <table class="table table-sm mb-0">
                <tbody>
                @foreach (var s in Model.ByStage)
                {
                    <tr><td>@s.Stage</td><td class="text-end">@s.Count</td></tr>
                }
                @if (Model.ByStage.Count == 0) { <tr><td class="text-muted">No active applications.</td></tr> }
                </tbody>
            </table>
        </div></div>
    </div>
    <div class="col-md-6">
        <div class="card h-100"><div class="card-body">
            <h2 class="h6">Recent applications</h2>
            <ul class="list-unstyled mb-0">
            @foreach (var r in Model.Recent)
            {
                <li class="d-flex justify-content-between border-bottom py-1">
                    <span>@r.Candidate <span class="text-muted small">- @r.Job</span></span>
                    <span class="text-muted small">@r.AppliedAt.ToLocalTime().ToString("g")</span>
                </li>
            }
            @if (Model.Recent.Count == 0) { <li class="text-muted">No applications yet.</li> }
            </ul>
        </div></div>
    </div>
</div>
```

- [ ] **Step 7: Build and run**

Run: `dotnet build` (0 errors), then run and confirm the dashboard shows real counts, a per-stage table,
and recent applications. Stop the app.

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): dashboard metrics"
```

---

## Task 6: Test-feed and connection on the Integration page

**Files:**
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs` (register the RT HttpClient here, shared).
- Modify: `src/Ats.Worker/Program.cs` (remove the now-duplicate registration).
- Modify: `src/Ats.Web/Models/IntegrationSettingsViewModel.cs`, `src/Ats.Web/Controllers/IntegrationController.cs`, `src/Ats.Web/Views/Integration/Index.cshtml`.

- [ ] **Step 1: Move the ReferralTool HttpClient registration into `AddAtsInfrastructure`**

In `DependencyInjection.cs`, add `using Ats.Application.Integration;` (already present) and before
`return services;`:

```csharp
        services.AddHttpClient<IReferralToolClient, ReferralToolClient>();
```

- [ ] **Step 2: Remove the duplicate from `src/Ats.Worker/Program.cs`**

Delete the line `builder.Services.AddHttpClient<IReferralToolClient, ReferralToolClient>();` (it is now
provided by `AddAtsInfrastructure`).

- [ ] **Step 3: Add `PublishedJobCount` to `IntegrationSettingsViewModel.cs`**

```csharp
    public int PublishedJobCount { get; set; }
```

- [ ] **Step 4: Update `IntegrationController.cs`**

Add `using Ats.Application.Integration;` if missing. Inject `IVacancyFeedRepository _feed` and
`IReferralToolClient _client` (constructor + fields). In `Index` GET, set the count on the view model:

```csharp
        var (_, total) = await _feed.GetPageAsync(1, 1);
        // ...assign on the returned view model:
        // vm.PublishedJobCount = total;
```
Concretely, replace the `Index` GET return with:

```csharp
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await _settings.GetAsync();
        var (_, total) = await _feed.GetPageAsync(1, 1);
        return View(new IntegrationSettingsViewModel
        {
            IntegrationEnabled = s.IntegrationEnabled,
            ReferralToolBaseUrl = s.ReferralToolBaseUrl,
            ReferralToolCustomerId = s.ReferralToolCustomerId,
            CodeParameterName = s.CodeParameterName,
            HasAuthToken = !string.IsNullOrEmpty(s.ReferralToolAuthToken),
            HasApiKey = !string.IsNullOrEmpty(s.ReferralToolApiKey),
            HasFeedKey = !string.IsNullOrEmpty(s.FeedApiKeyHash),
            PublishedJobCount = total
        });
    }
```

Add a `TestConnection` action:

```csharp
    [HttpPost]
    public async Task<IActionResult> TestConnection()
    {
        var s = await _settings.GetAsync();
        if (s.ReferralToolCustomerId is null || string.IsNullOrWhiteSpace(s.ReferralToolBaseUrl)
            || string.IsNullOrWhiteSpace(s.ReferralToolApiKey) || string.IsNullOrWhiteSpace(s.ReferralToolAuthToken))
        {
            TempData["Error"] = "Fill base URL, customer id, X-Api-Key, and X-Auth-Token first.";
            return RedirectToAction(nameof(Index));
        }

        var (page, _) = await _feed.GetPageAsync(1, 1);
        var sampleRef = page.FirstOrDefault()?.ExternalRef;
        if (sampleRef is null)
        {
            TempData["Error"] = "Publish a job first so there is a vacancy to test with.";
            return RedirectToAction(nameof(Index));
        }

        var settings = new ReferralToolSettings(s.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId.Value);
        var (result, exists) = await _client.CheckVacancyExistsAsync(settings, sampleRef);
        TempData[result.Reached && result.HttpStatus is >= 200 and < 300 ? "Success" : "Error"] =
            $"Test for {sampleRef}: reached={result.Reached}, HTTP {result.HttpStatus}, vacancy exists={exists}.";
        return RedirectToAction(nameof(Index));
    }
```

> `GetPageAsync` returns `(List<Job> Jobs, int Total)`; here `page` is the jobs list and `_` the total.

- [ ] **Step 5: Add the preview + test button to `Views/Integration/Index.cshtml`**

In the right-hand "Feed API key" card, after the existing "View delivery log" link, add:

```cshtml
            <hr />
            <p class="small mb-2">Published jobs the feed would return: <strong>@Model.PublishedJobCount</strong></p>
            <form asp-action="TestConnection" method="post">
                <button type="submit" class="btn btn-outline-secondary btn-sm">Test ReferralTool connection</button>
            </form>
```

- [ ] **Step 6: Build and run**

Run: `dotnet build` (0 errors), then run, sign in as Owner, open `/Integration`. Confirm the published-job
count shows, and Test connection reports a result (reachable/HTTP) without creating any application or
delivery row. Stop the app.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): feed preview and ReferralTool connection test"
```

---

## Task 7: Audit skill + index

**Files:**
- Create: `.claude/skills/audit/SKILL.md`.
- Modify: `CLAUDE.md`.

- [ ] **Step 1: Create `.claude/skills/audit/SKILL.md`**

```markdown
---
name: audit
description: The Ats audit log, dashboard metrics, and integration test tools - what is recorded, how, and where shown. Read before changing auditing or dashboard behavior.
---

# Ats Audit and Dashboards (Phase 4)

## Audit log
`AuditEntry` (TenantEntity): Action, EntityType, EntityRef, Summary, UserId, UserName, OccurredAt.
`IAuditLogger.LogAsync` writes one entry, stamping the user from `ICurrentUser` (the `Name` claim), and
never throws into the caller. Back-office controllers call it after successful mutations: job
create/publish/close/delete, pipeline save/delete, department/location create/update/delete, candidate
create/update, integration settings save, feed-key regenerate. Stage moves are not duplicated here; they
live in `ApplicationEvent` (application Details) and outbound deliveries in `WebhookDelivery` (Integration
delivery log). The Owner-only `AuditController` shows recent entries.

## Dashboard
`IDashboardService.GetAsync` returns a `DashboardSummary` (published jobs, candidates, active
applications, active-by-stage counts, recent applications), all tenant-scoped. `DashboardController`
renders it.

## Integration test tools
The Integration page shows the count of jobs the feed would return and a Test-connection button that
calls `IReferralToolClient.CheckVacancyExistsAsync` with the saved settings and a sample published
`ExternalRef`. It is a read-only probe: it never sends a status update or writes a `WebhookDelivery`.
The ReferralTool HttpClient is registered in `AddAtsInfrastructure` (shared by Web and Worker).
```

- [ ] **Step 2: Add the skill-index row to `CLAUDE.md`**

After the Integration row:

```markdown
| Audit | `.claude/skills/audit/SKILL.md` | Audit log, dashboard metrics, integration test tools |
```

- [ ] **Step 3: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit** (developer)

```bash
git add -A
git commit -m "docs: audit skill and skill-index"
```

---

## Task 8: Manual verification

**No new files.** Run in your session (LocalDB available).

- [ ] **Step 1:** Apply the `AddAuditEntry` migration (developer).
- [ ] **Step 2: Audit** - as Owner, publish a job, edit a pipeline, save integration settings; open `/Audit`
  and confirm each appears with user + timestamp. Confirm a non-Owner cannot see the sidebar entry or reach `/Audit`.
- [ ] **Step 3: Dashboard** - confirm the counts match the data, the per-stage table reflects active
  applications, and recent applications list.
- [ ] **Step 4: Integration** - confirm the published-job count is correct; click Test connection and
  confirm a result is reported without any new application or delivery row.
- [ ] **Step 5: Tenancy** - confirm a second tenant's audit, dashboard, and counts are isolated.
- [ ] **Step 6: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 4 Plan A audit/dashboards/test-feed verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage (Plan A):** `AuditEntry` + `IAuditLogger` (Task 1), migration (Task 2), audit calls at
  the spec's write points across six controllers (Task 3), Owner-only audit view + sidebar (Task 4),
  dashboard metrics service + wired view (Task 5), feed preview + read-only connection test with the
  shared RT HttpClient (Task 6), audit skill + index (Task 7), verification incl. tenancy and non-Owner
  denial (Task 8). Stage-move history intentionally not duplicated (linked instead), per the spec.
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion. Task 3 lists the
  exact `LogAsync` call per action; Task 1's `ICurrentUser.Name` addition backs `AuditLogger`.
- **Type consistency:** `IAuditLogger.LogAsync(string,string,string?,string,CancellationToken)` matches all
  call sites; `ICurrentUser.Name` added and used by `AuditLogger`; `DashboardSummary`/`StageCount`/
  `RecentApplication` match service and view; `IVacancyFeedRepository.GetPageAsync` reused for the count
  and sample ref; `IReferralToolClient.CheckVacancyExistsAsync` returns `(ReferralCallResult, bool)` as
  used; `AtsRole.Owner` gates the audit screen and sidebar entry.
- **HttpClient move:** registering `IReferralToolClient` in `AddAtsInfrastructure` and removing the worker
  duplicate keeps a single registration shared by Web and Worker.
- **Migration:** only `AddAuditEntry`. Confirmed.
- **Ordering:** every task builds green on its own; the audit calls (Task 3) depend on Task 1, the views
  on their services.
```
