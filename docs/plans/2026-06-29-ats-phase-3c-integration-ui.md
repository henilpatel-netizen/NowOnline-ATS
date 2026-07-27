# ATS Phase 3 - Plan C: Integration Settings UI and Delivery Log

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Owners a back-office page to configure the ReferralTool integration (base URL, auth token, API key, customer id, code parameter) and generate the feed API key, plus a read-only delivery log of outbox messages and their ReferralTool responses.

**Architecture:** An Application settings service reads/updates the current tenant's `TenantSettings` and generates the feed key (storing only its hash, via the Phase 3A `FeedApiKey` helper). A delivery-log service returns recent `OutboxMessage`s with their `WebhookDelivery` attempts. Thin Owner-only MVC controllers and Bootstrap views using the UI baseline.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, Bootstrap 5.

**Reference spec:** `docs/specs/2026-06-29-ats-phase-3-integration-design.md` (Plan C of three). No migration: all columns exist from Plans A/B. Generating the feed key here replaces the manual SQL step from Plan A Task 6.

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`; UI tasks add a manual run check.
- **Commits are manual** (developer). No migration.
- **Working directory** `D:\LiveProject\Ats`. Stop any running app before building.
- **No em dashes and no emoji** in generated files.
- Services/repositories register in `Ats.Infrastructure/DependencyInjection.cs`.
- Owner-only screens use `[Authorize(Roles = AtsRole.Owner)]` (the `ClaimTypes.Role` claim is set at sign-in).

---

## File structure (created or modified)

```
src\Ats.Application\Integration\IIntegrationSettingsService.cs, IntegrationSettingsInput.cs   # NEW
src\Ats.Application\Integration\IDeliveryLogService.cs, DeliveryLogEntry.cs                    # NEW
src\Ats.Infrastructure\Integration\IntegrationSettingsService.cs, DeliveryLogService.cs        # NEW
src\Ats.Infrastructure\DependencyInjection.cs                                                 # MODIFY
src\Ats.Web\Models\IntegrationSettingsViewModel.cs                                            # NEW
src\Ats.Web\Controllers\IntegrationController.cs                                              # NEW
src\Ats.Web\Views\Integration\Index.cshtml, Deliveries.cshtml                                 # NEW
src\Ats.Web\ViewComponents\SidebarNavViewComponent.cs                                         # MODIFY: role-aware nav + Integration entry
.claude\skills\integration\SKILL.md                                                           # NEW
CLAUDE.md                                                                                      # MODIFY: skill-index
docs\integration\referraltool-contract.md                                                     # MODIFY: record verified drift
```

---

## Task 1: Integration settings service

**Files:**
- Create: `src/Ats.Application/Integration/IntegrationSettingsInput.cs`, `IIntegrationSettingsService.cs`.
- Create: `src/Ats.Infrastructure/Integration/IntegrationSettingsService.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `IntegrationSettingsInput.cs`** (secrets are null = keep existing)

```csharp
namespace Ats.Application.Integration;

public record IntegrationSettingsInput(
    bool IntegrationEnabled,
    string? ReferralToolBaseUrl,
    int? ReferralToolCustomerId,
    string CodeParameterName,
    string? ReferralToolAuthToken,   // null/blank = keep existing
    string? ReferralToolApiKey);     // null/blank = keep existing
```

- [ ] **Step 2: Create `IIntegrationSettingsService.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IIntegrationSettingsService
{
    Task<TenantSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default);
    // Generates a new feed key, stores only its hash, and returns the plaintext (shown once).
    Task<string> GenerateFeedKeyAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `IntegrationSettingsService.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly AtsDbContext _db;
    public IntegrationSettingsService(AtsDbContext db) => _db = db;

    public async Task<TenantSettings> GetAsync(CancellationToken ct = default)
    {
        // Every tenant has exactly one settings row (created at onboarding).
        return await _db.TenantSettings.FirstAsync(ct);
    }

    public async Task UpdateAsync(IntegrationSettingsInput input, CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct);
        settings.IntegrationEnabled = input.IntegrationEnabled;
        settings.ReferralToolBaseUrl = string.IsNullOrWhiteSpace(input.ReferralToolBaseUrl) ? null : input.ReferralToolBaseUrl.Trim();
        settings.ReferralToolCustomerId = input.ReferralToolCustomerId;
        settings.CodeParameterName = string.IsNullOrWhiteSpace(input.CodeParameterName) ? "ref" : input.CodeParameterName.Trim();

        // Secrets: only overwrite when a new non-blank value is supplied.
        if (!string.IsNullOrWhiteSpace(input.ReferralToolAuthToken))
            settings.ReferralToolAuthToken = input.ReferralToolAuthToken.Trim();
        if (!string.IsNullOrWhiteSpace(input.ReferralToolApiKey))
            settings.ReferralToolApiKey = input.ReferralToolApiKey.Trim();

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> GenerateFeedKeyAsync(CancellationToken ct = default)
    {
        var key = FeedApiKey.Generate();
        var settings = await _db.TenantSettings.FirstAsync(ct);
        settings.FeedApiKeyHash = FeedApiKey.Hash(key);
        await _db.SaveChangesAsync(ct);
        return key;
    }
}
```

- [ ] **Step 4: Register in `DependencyInjection.cs`**

Before `return services;`:

```csharp
        services.AddScoped<IIntegrationSettingsService, IntegrationSettingsService>();
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat: integration settings service with feed-key generation"
```

---

## Task 2: Delivery log service

**Files:**
- Create: `src/Ats.Application/Integration/DeliveryLogEntry.cs`, `IDeliveryLogService.cs`.
- Create: `src/Ats.Infrastructure/Integration/DeliveryLogService.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `DeliveryLogEntry.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public sealed record DeliveryLogEntry(OutboxMessage Message, IReadOnlyList<WebhookDelivery> Deliveries);
```

- [ ] **Step 2: Create `IDeliveryLogService.cs`**

```csharp
namespace Ats.Application.Integration;

public interface IDeliveryLogService
{
    // Most recent outbox messages for the current tenant, each with its delivery attempts.
    Task<List<DeliveryLogEntry>> RecentAsync(int take = 200, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `DeliveryLogService.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class DeliveryLogService : IDeliveryLogService
{
    private readonly AtsDbContext _db;
    public DeliveryLogService(AtsDbContext db) => _db = db;

    public async Task<List<DeliveryLogEntry>> RecentAsync(int take = 200, CancellationToken ct = default)
    {
        var messages = await _db.OutboxMessages
            .OrderByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();
        var deliveries = await _db.WebhookDeliveries
            .Where(d => ids.Contains(d.OutboxMessageId))
            .ToListAsync(ct);

        return messages
            .Select(m => new DeliveryLogEntry(
                m,
                deliveries.Where(d => d.OutboxMessageId == m.Id).OrderBy(d => d.Id).ToList()))
            .ToList();
    }
}
```

- [ ] **Step 4: Register in `DependencyInjection.cs`**

Before `return services;`:

```csharp
        services.AddScoped<IDeliveryLogService, DeliveryLogService>();
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat: delivery log service"
```

---

## Task 3: Integration controller, views, and sidebar

**Files:**
- Create: `src/Ats.Web/Models/IntegrationSettingsViewModel.cs`.
- Create: `src/Ats.Web/Controllers/IntegrationController.cs`.
- Create: `src/Ats.Web/Views/Integration/Index.cshtml`, `Deliveries.cshtml`.
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`.

- [ ] **Step 1: Create `IntegrationSettingsViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class IntegrationSettingsViewModel
{
    public bool IntegrationEnabled { get; set; }
    [StringLength(300)] public string? ReferralToolBaseUrl { get; set; }
    public int? ReferralToolCustomerId { get; set; }
    [Required, StringLength(40)] public string CodeParameterName { get; set; } = "ref";

    // Secrets: blank means keep the stored value.
    [StringLength(500)] public string? ReferralToolAuthToken { get; set; }
    [StringLength(500)] public string? ReferralToolApiKey { get; set; }

    // Display-only flags so the view can show "configured" without revealing secrets.
    public bool HasAuthToken { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasFeedKey { get; set; }
}
```

- [ ] **Step 2: Create `IntegrationController.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Roles = AtsRole.Owner)]
public class IntegrationController : Controller
{
    private readonly IIntegrationSettingsService _settings;
    private readonly IDeliveryLogService _log;

    public IntegrationController(IIntegrationSettingsService settings, IDeliveryLogService log)
    {
        _settings = settings; _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await _settings.GetAsync();
        return View(new IntegrationSettingsViewModel
        {
            IntegrationEnabled = s.IntegrationEnabled,
            ReferralToolBaseUrl = s.ReferralToolBaseUrl,
            ReferralToolCustomerId = s.ReferralToolCustomerId,
            CodeParameterName = s.CodeParameterName,
            HasAuthToken = !string.IsNullOrEmpty(s.ReferralToolAuthToken),
            HasApiKey = !string.IsNullOrEmpty(s.ReferralToolApiKey),
            HasFeedKey = !string.IsNullOrEmpty(s.FeedApiKeyHash)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(IntegrationSettingsViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _settings.UpdateAsync(new IntegrationSettingsInput(
            vm.IntegrationEnabled, vm.ReferralToolBaseUrl, vm.ReferralToolCustomerId,
            vm.CodeParameterName, vm.ReferralToolAuthToken, vm.ReferralToolApiKey));
        TempData["Success"] = "Integration settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> GenerateFeedKey()
    {
        var key = await _settings.GenerateFeedKeyAsync();
        TempData["FeedKey"] = key;   // shown once
        TempData["Success"] = "New feed API key generated. Copy it now; it will not be shown again.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Deliveries()
    {
        return View(await _log.RecentAsync());
    }
}
```

- [ ] **Step 3: Create `Views/Integration/Index.cshtml`**

```cshtml
@model Ats.Web.Models.IntegrationSettingsViewModel
@{ ViewData["Title"] = "Integration"; }

@if (TempData["FeedKey"] is string newKey)
{
    <div class="alert alert-warning">
        <div class="fw-semibold mb-1">New feed API key (copy now, shown once):</div>
        <code>@newKey</code>
    </div>
}

<div class="row">
    <div class="col-lg-7">
        <form asp-action="Index" method="post" class="card card-body mb-4">
            <div asp-validation-summary="All" class="text-danger small mb-2"></div>
            <div class="form-check mb-3">
                <input asp-for="IntegrationEnabled" class="form-check-input" />
                <label asp-for="IntegrationEnabled" class="form-check-label">Integration enabled</label>
            </div>
            <div class="mb-3">
                <label asp-for="ReferralToolBaseUrl" class="form-label">ReferralTool base URL</label>
                <input asp-for="ReferralToolBaseUrl" class="form-control" placeholder="https://referraltool.example.com" />
            </div>
            <div class="mb-3">
                <label asp-for="ReferralToolCustomerId" class="form-label">ReferralTool customer id</label>
                <input asp-for="ReferralToolCustomerId" class="form-control" type="number" />
            </div>
            <div class="mb-3">
                <label asp-for="CodeParameterName" class="form-label">Referral code parameter name</label>
                <input asp-for="CodeParameterName" class="form-control" />
            </div>
            <div class="mb-3">
                <label asp-for="ReferralToolAuthToken" class="form-label">X-Auth-Token @(Model.HasAuthToken ? "(set; leave blank to keep)" : "")</label>
                <input asp-for="ReferralToolAuthToken" class="form-control" type="password" autocomplete="off" />
            </div>
            <div class="mb-3">
                <label asp-for="ReferralToolApiKey" class="form-label">X-Api-Key @(Model.HasApiKey ? "(set; leave blank to keep)" : "")</label>
                <input asp-for="ReferralToolApiKey" class="form-control" type="password" autocomplete="off" />
            </div>
            <button type="submit" class="btn btn-primary">Save</button>
        </form>
    </div>
    <div class="col-lg-5">
        <div class="card card-body">
            <h2 class="h6">Feed API key</h2>
            <p class="text-muted small mb-2">ReferralTool uses this to pull the vacancy feed. Only its hash is stored.</p>
            <p class="mb-2">Status: @(Model.HasFeedKey ? "configured" : "not set")</p>
            <form asp-action="GenerateFeedKey" method="post"
                  onsubmit="return confirm('Generate a new feed key? The old key stops working immediately.');">
                <button type="submit" class="btn btn-outline-secondary btn-sm">@(Model.HasFeedKey ? "Regenerate" : "Generate") feed key</button>
            </form>
            <hr />
            <a asp-action="Deliveries" class="btn btn-outline-secondary btn-sm">View delivery log</a>
        </div>
    </div>
</div>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 4: Create `Views/Integration/Deliveries.cshtml`**

```cshtml
@model List<Ats.Application.Integration.DeliveryLogEntry>
@using Ats.Domain.Enums
@{ ViewData["Title"] = "Delivery log"; }
<div class="mb-3"><a class="btn btn-outline-secondary btn-sm" asp-action="Index"><i class="bi bi-arrow-left"></i> Integration</a></div>
<table class="table bg-white align-middle">
    <thead><tr><th>#</th><th>Candidate ext id</th><th>Vacancy</th><th>Status sent</th><th>State</th><th>Attempts</th><th>Last attempt</th></tr></thead>
    <tbody>
    @foreach (var e in Model)
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
    @if (Model.Count == 0) { <tr><td colspan="7" class="text-muted">No status updates yet.</td></tr> }
    </tbody>
</table>
```

- [ ] **Step 5: Make the sidebar role-aware and add the Integration entry**

In `SidebarNavViewComponent.cs`, change the `NavItem` record to carry an optional required role, add the
Integration item, and filter by the current user's role. Replace the record and the `Items` array and the
`Invoke` body with:

```csharp
public record NavItem(string Text, string Icon, string Controller, string Action, string? RequiredRole = null);

public record SidebarNavModel(IReadOnlyList<NavItem> Items, string CurrentController, string UserName, string Role);

public class SidebarNavViewComponent : ViewComponent
{
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "bi-speedometer2", "Dashboard", "Index"),
        new("Jobs", "bi-briefcase", "Jobs", "Index"),
        new("Pipelines", "bi-diagram-3", "Pipelines", "Index"),
        new("Candidates", "bi-people", "Candidates", "Index"),
        new("Departments", "bi-building", "Departments", "Index"),
        new("Locations", "bi-geo-alt", "Locations", "Index"),
        new("Integration", "bi-plugin", "Integration", "Index", AtsRole.Owner),
    };

    public IViewComponentResult Invoke()
    {
        var current = RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var name = User.Identity?.Name is { Length: > 0 } n ? n : "User";
        var role = (User as ClaimsPrincipal)?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var visible = Items.Where(i => i.RequiredRole is null || string.Equals(i.RequiredRole, role, StringComparison.OrdinalIgnoreCase)).ToList();
        return View(new SidebarNavModel(visible, current, name, role));
    }
}
```

Add `using Ats.Domain.Enums;` to the top of the file (for `AtsRole`).

- [ ] **Step 6: Build and run**

Run: `dotnet build` (expected 0 errors), then `dotnet run --project src/Ats.Web`, sign in as the Owner.
Browse `/Integration`: toggle enabled, enter base URL / customer id / code param / tokens, Save; generate
a feed key (confirm it shows once); open the delivery log. Confirm a non-Owner does not see the
Integration sidebar entry and gets denied at `/Integration`. Stop the app.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): integration settings and delivery log (Owner-only)"
```

---

## Task 4: Phase 3 documentation

**Files:**
- Create: `.claude/skills/integration/SKILL.md`.
- Modify: `CLAUDE.md`, `docs/integration/referraltool-contract.md`.

- [ ] **Step 1: Create `.claude/skills/integration/SKILL.md`**

```markdown
---
name: integration
description: The Ats - ReferralTool integration - vacancy feed, feed-key auth, outbox enqueue, the worker delivery loop, the ReferralTool client, and integration settings. Read before changing any integration behavior.
---

# Ats - ReferralTool Integration (Phase 3)

## Vacancy feed (Ats.Api)
`POST /jobs/search` returns the tenant's non-draft jobs in the CatsOne shape (`type":"H"`,
`location.city`, `status.title` Actief for Published, non-Actief for Closed; Draft excluded). Auth is a
per-tenant feed key sent as `Authorization: Token {key}`; `FeedApiKeyFilter` SHA-256-matches
`TenantSettings.FeedApiKeyHash` (IgnoreQueryFilters) and sets `HttpContext.Items["TenantId"]`. Dev-only
Scalar UI at `/scalar`.

## Outbox enqueue
`IOutboxEnqueuer.StageAsync` adds an `OutboxMessage` (payload snapshot) in the same unit of work as the
stage `ApplicationEvent`, only on first arrival at a stage, when the application has a `SourceCode` and
`TenantSettings.IntegrationEnabled` with a `ReferralToolCustomerId`. Wired into
`ApplicationService.MoveStageAsync`/`CreateApplicationAsync` and `CareerService.ApplyAsync`. Mapping:
`Code`=SourceCode, `ExternalVacancyId`=Job.ExternalRef, `ExternalCandidateId`=Candidate.Key,
`CandidateStatus`=stage.ReferralStatusOverride ?? stage.Name.

## Worker delivery (Ats.Worker)
`OutboxDrainer` polls every `Integration:PollSeconds`. `OutboxClaimStore` claims due Pending messages
across all tenants (IgnoreQueryFilters); per message the `OutboxProcessor` sets
`WorkerTenantContext.TenantId`, pre-checks the vacancy (`checkvacancyexists`), and posts
`candidatestatusupdate` via `IReferralToolClient` with `X-Api-Key` + `X-Auth-Token`. Outcomes: 2xx
Delivered; 5xx/timeout transient (exponential backoff, dead-letter after `MaxAttempts`); 4xx terminal;
vacancy-not-imported is transient. Per-application ordering: stop a chain on the first non-delivered.
Every attempt logs a `WebhookDelivery`.

## Settings and log (back-office, Owner-only)
`IntegrationController` edits `TenantSettings` integration fields and generates the feed key (hash
stored, plaintext shown once). `Deliveries` shows recent `OutboxMessage`s with their `WebhookDelivery`
attempts.

## Contract
The frozen ReferralTool contract is `docs/integration/referraltool-contract.md`. The status route is
`/v1.0/kafka/candidatestatusupdate` with dual `X-Api-Key` + `X-Auth-Token`.
```

- [ ] **Step 2: Add the skill-index row to `CLAUDE.md`**

After the Career site row:

```markdown
| Integration | `.claude/skills/integration/SKILL.md` | Feed, outbox, worker, ReferralTool client, settings |
```

- [ ] **Step 3: Record the verified drift in `docs/integration/referraltool-contract.md`**

Append a dated section at the end of the file:

```markdown
---

## Verified against ReferralTool source on 2026-06-29 (supersedes the 2026-06-26 notes above)

1. Status route is `POST /v1.0/kafka/candidatestatusupdate` (controller `Kafka`, version 1.0).
2. The status endpoint requires BOTH headers: `X-Api-Key` (a ReferralTool-issued key, validated by the
   `[Authorize(ApiKey)]` scheme) AND `X-Auth-Token` (compared to `Kafka:AuthToken`). The 2026-06-26
   Appendix B documented only `X-Auth-Token`.
3. "Vacancy does not exist" returns HTTP 400 (transient until the feed import lands). A pre-flight
   `POST /v1.0/kafka/checkvacancyexists` (`{ CustomerId, ExternalVacancyId }` -> `{ exists }`, same dual
   auth) is available and is used by the worker.
4. Feed hours `custom_fields` use a nested shape: each entry's name is at
   `custom_fields[]._embedded.definition.name` (value at `[].value`), not the flat `{name,value}` shown
   in Appendix A. The Ats feed omits `custom_fields` in v1 (hours optional).
```

- [ ] **Step 4: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "docs: integration skill, skill-index, and verified contract drift"
```

---

## Task 5: Manual end-to-end verification (Phase 3 complete)

**No new files.** Run in your session (LocalDB available).

- [ ] **Step 1: Settings** - sign in as Owner, open `/Integration`, enable integration, set base URL,
  customer id, code param, `X-Auth-Token`, `X-Api-Key`; Save; reopen and confirm values persist and
  secrets show as "set" (blank inputs).

- [ ] **Step 2: Feed key** - click Generate; confirm the plaintext shows once. Use it to call the feed
  from Scalar (`/scalar`, `POST /jobs/search`, Authorize with `Token {key}`); confirm published jobs
  return. Regenerate and confirm the old key now returns 401.

- [ ] **Step 3: Outbox + delivery log** - apply on the career site with a `?ref=` code (or move a referred
  candidate). Run `Ats.Worker`. Open `/Integration` -> View delivery log; confirm the message and its
  `CheckVacancy`/`StatusUpdate` attempts appear with state and HTTP status. Against a configured
  ReferralTool test customer it reads Delivered; otherwise it stays Pending with attempts (transient).

- [ ] **Step 4: Authorization** - sign in as a non-Owner (create a Recruiter user, or temporarily set a
  user's role) and confirm the Integration sidebar entry is hidden and `/Integration` is denied.

- [ ] **Step 5: Full loop (if a ReferralTool test customer is configured)** - publish a job, let
  ReferralTool import it via the feed, share it, apply via the `?ref=` link, move the candidate, and
  confirm ReferralTool creates the candidate and credits the referrer, with the delivery log showing the
  request and 2xx response.

- [ ] **Step 6: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 3 integration complete and verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage (Plan C):** integration-settings service with secret-preserving update and feed-key
  generation (Task 1); delivery-log service (Task 2); Owner-only `IntegrationController` + settings form
  (with masked secrets and shown-once key) + delivery-log view + role-aware sidebar entry (Task 3);
  integration skill, CLAUDE.md skill-index, and the verified contract-drift record (Task 4); verification
  incl. settings, feed key, delivery log, authorization, and the full loop (Task 5). This also closes the
  Plan A manual-key gap (the key is now generated in the UI).
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion.
- **Type consistency:** `IntegrationSettingsInput` matches the service and the controller mapping;
  `IIntegrationSettingsService`/`IDeliveryLogService` signatures match impl and controller; `DeliveryLogEntry`
  (`Message`, `Deliveries`) matches the view; `FeedApiKey.Generate/Hash` reused from Plan A;
  `OutboxStatus`/`DeliveryKind` reused from Plan B; `AtsRole.Owner` used for `[Authorize(Roles=...)]` and
  the nav filter; the `NavItem` record gains an optional `RequiredRole` with a default so existing
  entries are unchanged.
- **No migration:** all columns exist from Plans A/B. Confirmed.
- **Ordering:** every task builds green on its own; the controller (Task 3) depends on the services
  (Tasks 1-2).
```
