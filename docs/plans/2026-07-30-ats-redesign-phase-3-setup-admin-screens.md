# ATS NowOnline Redesign - Phase 3: Setup, Admin, and Career-site back office

> **For agentic workers:** checkbox (`- [ ]`) steps, worked in order.
>
> **Repo constraints (unchanged):** no `git` commands (each task ends with Verify; commit points are
> marked for the developer) and no `dotnet ef database update`. **Phase 3 adds no migration** — the
> `FeedLastPulledAt` and branding columns already shipped in Phase 1.
>
> **Visual source of truth:** `ATS - Redesign.dc.html`. Line refs: pipelines L646-752,
> organisation L754-776, integrations L778-864, audit L867-905, career site L907-968.
> Component classes exist in `wwwroot/css/ats-components.css`; the browser-frame class
> (`.ats-browser-frame`/`.ats-browser-chrome`/`.ats-browser-url`) was added in Phase 1.

**Goal:** Rebuild the four remaining back-office screens (pipelines, organisation, integrations,
audit) into the prototype layouts, add the career-site back office and its branding editor (wiring
the branding pipeline built in Phase 1), and wire the `FeedLastPulledAt` write so the integration
health panels stop reading "never". Merge Departments + Locations under `/Organisation`, redirecting
the old routes.

**Architecture:** New read model for organisation job counts (`Ats.Application/Organisation` +
`Ats.Infrastructure/Organisation`). Audit gains a filtered/paged query alongside its existing
`RecentAsync`. The branding editor reuses `ITenantBrandingService.UpdateAsync` from Phase 1. The
feed-pull write is a debounced, failure-swallowed touch from `Ats.Api`'s `FeedController`, decided by
a pure throttle helper (unit-tested). Controllers stay thin; EF stays in Infrastructure.

**Tech Stack:** unchanged.

**Spec:** `docs/specs/2026-07-30-ats-nowonline-redesign-design.md`
**Prior phases:** phase-1-foundation, phase-2-recruiting-screens (both committed).

**Routing note.** `OrganisationController` and `CareerSiteController` are conventional
(`{controller}/{action}`). `CareerSite` is deliberately not named `Careers`: the public site owns the
literal attribute route `careers/{slug}` (`Ats.Web/Areas/Careers`), and a literal segment wins over a
conventional one, so `/Careers/...` would resolve to the public site with `slug="..."` and 404.
`/CareerSite/...` cannot collide.

---

## File Structure

### Created

| File | Responsibility |
|---|---|
| `tests/Ats.Tests/Integration/FeedPullThrottleTests.cs` | debounce decision |
| `src/Ats.Application/Common/FeedPullThrottle.cs` | pure "should record this pull?" |
| `src/Ats.Application/Organisation/OrganisationOverview.cs` | overview record + `IOrganisationReadService` |
| `src/Ats.Infrastructure/Organisation/OrganisationReadService.cs` | EF projection with job counts |
| `src/Ats.Web/Controllers/OrganisationController.cs` | `/Organisation` |
| `src/Ats.Web/Controllers/CareerSiteController.cs` | `/CareerSite` + `/CareerSite/Branding` |
| `src/Ats.Web/Models/OrganisationViewModel.cs` | overview view model |
| `src/Ats.Web/Models/BrandingEditViewModel.cs` | branding form model |
| `src/Ats.Web/Views/Organisation/Index.cshtml` | departments + locations, job counts |
| `src/Ats.Web/Views/CareerSite/Index.cshtml` | browser-frame preview |
| `src/Ats.Web/Views/CareerSite/Branding.cshtml` | branding editor |
| `src/Ats.Web/Views/Integration/_DeliveryRows.cshtml` | shared delivery-log rows partial |

### Modified

| File | Change |
|---|---|
| `src/Ats.Application/Integration/IVacancyFeedRepository.cs` | add `TouchFeedPulledAsync` |
| `src/Ats.Infrastructure/Persistence/Repositories/VacancyFeedRepository.cs` | implement it (debounced) |
| `src/Ats.Api/Controllers/FeedController.cs` | call the touch, swallow failures |
| `src/Ats.Application/Auditing/IAuditQuery` (in `IAuditLogger.cs`) | add filtered/paged method |
| `src/Ats.Infrastructure/Auditing/AuditQuery.cs` | implement it |
| `src/Ats.Web/Controllers/AuditController.cs` | filter + page params |
| `src/Ats.Web/Controllers/DepartmentsController.cs` | `Index` redirects to `/Organisation` |
| `src/Ats.Web/Controllers/LocationsController.cs` | `Index` redirects to `/Organisation` |
| `src/Ats.Infrastructure/DependencyInjection.cs` | register `IOrganisationReadService` |
| `src/Ats.Web/Models/AuditIndexViewModel.cs` | new (filters + paged results) |
| `src/Ats.Web/Views/Pipelines/Index.cshtml` | rebuilt (template cards) |
| `src/Ats.Web/Views/Pipelines/Form.cshtml` | rebuilt (stage editor) |
| `src/Ats.Web/Views/Integration/Index.cshtml` | rebuilt (health banner, form, feed key, inline log) |
| `src/Ats.Web/Views/Integration/Deliveries.cshtml` | rebuilt (uses `_DeliveryRows`) |
| `src/Ats.Web/Views/Audit/Index.cshtml` | rebuilt (icon timeline, filters, pager) |
| `src/Ats.Web/Views/Departments/Form.cshtml` | restyled |
| `src/Ats.Web/Views/Locations/Form.cshtml` | restyled |

---

## Task 1: Feed-pull throttle (TDD) + write

**Files:**
- Create: `tests/Ats.Tests/Integration/FeedPullThrottleTests.cs`
- Create: `src/Ats.Application/Common/FeedPullThrottle.cs`
- Modify: `src/Ats.Application/Integration/IVacancyFeedRepository.cs`
- Modify: `src/Ats.Infrastructure/Persistence/Repositories/VacancyFeedRepository.cs`
- Modify: `src/Ats.Api/Controllers/FeedController.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Integration;

public class FeedPullThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_when_never_pulled_before()
    {
        Assert.True(FeedPullThrottle.ShouldRecord(null, Now));
    }

    [Fact]
    public void Records_when_last_pull_is_over_a_minute_ago()
    {
        Assert.True(FeedPullThrottle.ShouldRecord(Now.AddSeconds(-61), Now));
    }

    [Fact]
    public void Skips_when_last_pull_is_within_the_minute()
    {
        Assert.False(FeedPullThrottle.ShouldRecord(Now.AddSeconds(-59), Now));
    }

    [Fact]
    public void Skips_a_duplicate_at_the_same_instant()
    {
        Assert.False(FeedPullThrottle.ShouldRecord(Now, Now));
    }

    [Fact]
    public void Records_when_the_clock_appears_to_go_backwards()
    {
        // A stored timestamp in the future (clock skew) should not wedge the throttle shut forever;
        // treat it as due.
        Assert.True(FeedPullThrottle.ShouldRecord(Now.AddSeconds(120), Now));
    }
}
```

- [ ] **Step 2: Run - expect compile failure**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: `FeedPullThrottle` does not exist.

- [ ] **Step 3: Implement the helper**

`src/Ats.Application/Common/FeedPullThrottle.cs`:

```csharp
namespace Ats.Application.Common;

// The vacancy feed can be pulled many times a minute; we only persist the timestamp at most once a
// minute to avoid a write on every request. A future stored value (clock skew) counts as due.
public static class FeedPullThrottle
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    public static bool ShouldRecord(DateTimeOffset? lastPulledAt, DateTimeOffset now)
    {
        if (lastPulledAt is null) return true;
        var elapsed = now - lastPulledAt.Value;
        return elapsed >= MinInterval || elapsed < TimeSpan.Zero;
    }
}
```

- [ ] **Step 4: Run - expect pass**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: all pass.

- [ ] **Step 5: Add the repository touch**

In `IVacancyFeedRepository` (`src/Ats.Application/Integration/IVacancyFeedRepository.cs`):

```csharp
    // Records "the feed was pulled just now" on the current tenant's settings, debounced to at most
    // once a minute. Safe to call on every feed request.
    Task TouchFeedPulledAsync(CancellationToken ct = default);
```

Implement in `VacancyFeedRepository`. Read the current value, decide with the throttle, write only if
due. It runs under the tenant set by `FeedApiKeyFilter`, so `TenantSettings` is filtered to the right
tenant with no predicate. Read the file first to match its context field name and usings.

```csharp
using Ats.Application.Common;
// ... existing usings ...

    public async Task TouchFeedPulledAsync(CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (settings is null) return;
        var now = DateTimeOffset.UtcNow;
        if (!FeedPullThrottle.ShouldRecord(settings.FeedLastPulledAt, now)) return;
        settings.FeedLastPulledAt = now;
        await _db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 6: Call it from the feed endpoint, swallowing failures**

In `FeedController.Search`, after `GetPageAsync`, record the pull. A telemetry write must never fail
the feed response, so wrap it and never let it throw. Inject an `ILogger<FeedController>` to log.

```csharp
    private readonly IVacancyFeedRepository _feed;
    private readonly ILogger<FeedController> _logger;
    public FeedController(IVacancyFeedRepository feed, ILogger<FeedController> logger)
    {
        _feed = feed;
        _logger = logger;
    }
```

After building `response` and before `return response;`:

```csharp
        try { await _feed.TouchFeedPulledAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to record feed pull timestamp"); }
```

Add `using Microsoft.Extensions.Logging;` to the controller.

- [ ] **Step 7: Verify**

Run: `dotnet build`
Expected: success (both `Ats.Api` and `Ats.Web` reference `IVacancyFeedRepository`).

*Commit point: `feat: record vacancy feed pull timestamp (debounced)`*

---

## Task 2: Organisation read service

**Files:**
- Create: `src/Ats.Application/Organisation/OrganisationOverview.cs`
- Create: `src/Ats.Infrastructure/Organisation/OrganisationReadService.cs`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Contract**

`src/Ats.Application/Organisation/OrganisationOverview.cs`:

```csharp
namespace Ats.Application.Organisation;

public sealed record OrgDepartment(int Id, string Name, int JobCount);
public sealed record OrgLocation(int Id, string Name, string? City, int JobCount);
public sealed record OrganisationOverview(
    IReadOnlyList<OrgDepartment> Departments,
    IReadOnlyList<OrgLocation> Locations);

public interface IOrganisationReadService
{
    Task<OrganisationOverview> GetAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: EF projection**

`src/Ats.Infrastructure/Organisation/OrganisationReadService.cs`. Job counts exclude soft-deleted
jobs, which the global query filter already does for `_db.Jobs`.

```csharp
using Ats.Application.Organisation;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Organisation;

public sealed class OrganisationReadService : IOrganisationReadService
{
    private readonly AtsDbContext _db;
    public OrganisationReadService(AtsDbContext db) => _db = db;

    public async Task<OrganisationOverview> GetAsync(CancellationToken ct = default)
    {
        var deptCounts = await _db.Jobs.Where(j => j.DepartmentId != null)
            .GroupBy(j => j.DepartmentId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var locCounts = await _db.Jobs.Where(j => j.LocationId != null)
            .GroupBy(j => j.LocationId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var departments = await _db.Departments.OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name }).ToListAsync(ct);
        var locations = await _db.Locations.OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.City }).ToListAsync(ct);

        return new OrganisationOverview(
            departments.Select(d => new OrgDepartment(d.Id, d.Name, deptCounts.TryGetValue(d.Id, out var dc) ? dc : 0)).ToList(),
            locations.Select(l => new OrgLocation(l.Id, l.Name, l.City, locCounts.TryGetValue(l.Id, out var lc) ? lc : 0)).ToList());
    }
}
```

- [ ] **Step 3: Register**

In `DependencyInjection.cs` add `using Ats.Application.Organisation;` and
`using Ats.Infrastructure.Organisation;`, then:

```csharp
        services.AddScoped<IOrganisationReadService, OrganisationReadService>();
```

- [ ] **Step 4: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: organisation overview read service`*

---

## Task 3: Organisation controller + view; redirect old routes

**Files:**
- Create: `src/Ats.Web/Controllers/OrganisationController.cs`
- Create: `src/Ats.Web/Models/OrganisationViewModel.cs`
- Create: `src/Ats.Web/Views/Organisation/Index.cshtml`
- Modify: `src/Ats.Web/Controllers/DepartmentsController.cs`
- Modify: `src/Ats.Web/Controllers/LocationsController.cs`

- [ ] **Step 1: View model + controller**

`src/Ats.Web/Models/OrganisationViewModel.cs`:

```csharp
using Ats.Application.Organisation;

namespace Ats.Web.Models;

public class OrganisationViewModel
{
    public OrganisationOverview Overview { get; set; } = default!;
}
```

`src/Ats.Web/Controllers/OrganisationController.cs`:

```csharp
using Ats.Application.Organisation;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class OrganisationController : Controller
{
    private readonly IOrganisationReadService _org;
    public OrganisationController(IOrganisationReadService org) => _org = org;

    public async Task<IActionResult> Index()
        => View(new OrganisationViewModel { Overview = await _org.GetAsync() });
}
```

- [ ] **Step 2: Redirect the old Index actions**

In `DepartmentsController`, replace the `Index` action body:

```csharp
    public IActionResult Index() => RedirectToActionPermanent("Index", "Organisation");
```

Same in `LocationsController`. Every other action (Create/Edit/Delete) is untouched, so existing
create/edit/delete links and posts keep working; only the list pages move. `_service` may now be
unused in a controller if `Index` was its only reader — leave the field; Create/Edit/Delete still use
it. (Both controllers use `_service` in other actions, so no unused-field warning.)

- [ ] **Step 3: Build the view**

`src/Ats.Web/Views/Organisation/Index.cshtml`. Prototype L754-776: two `.ats-card-flush` cards
(Departments, Locations) side by side, each row showing the name (and city for locations), a mono
job-count, and an edit action; each card header has an Add button linking to the respective
controller's Create.

```razor
@model Ats.Web.Models.OrganisationViewModel
@{
    ViewData["Title"] = "Organisation";
    ViewData["Eyebrow"] = "Departments and locations in one place:";
}

<div class="row g-3 align-items-start">
    <div class="col-lg-6">
        <div class="ats-card-flush">
            <div class="ats-card-head">
                <span class="ats-card-title">Departments</span>
                <a class="btn btn-sm btn-outline-secondary" asp-controller="Departments" asp-action="Create"><span class="ms ms-sm">add</span> Add</a>
            </div>
            @foreach (var d in Model.Overview.Departments)
            {
                <div class="ats-trow" style="grid-template-columns:1fr auto auto;gap:1rem">
                    <span class="ats-small">@d.Name</span>
                    <span class="ats-mono ats-xsmall ats-muted">@d.JobCount @(d.JobCount == 1 ? "job" : "jobs")</span>
                    <a class="ms ms-sm ats-faint" asp-controller="Departments" asp-action="Edit" asp-route-id="@d.Id" aria-label="Edit @d.Name">edit</a>
                </div>
            }
            @if (Model.Overview.Departments.Count == 0)
            {
                <partial name="Partials/_EmptyState" model="@(new Ats.Web.Models.Shared.EmptyStateModel("apartment", "No departments yet."))" />
            }
        </div>
    </div>
    <div class="col-lg-6">
        <div class="ats-card-flush">
            <div class="ats-card-head">
                <span class="ats-card-title">Locations</span>
                <a class="btn btn-sm btn-outline-secondary" asp-controller="Locations" asp-action="Create"><span class="ms ms-sm">add</span> Add</a>
            </div>
            @foreach (var l in Model.Overview.Locations)
            {
                <div class="ats-trow" style="grid-template-columns:1fr auto auto;gap:1rem">
                    <span class="ats-cell-stack">
                        <span class="ats-small">@l.Name</span>
                        @if (!string.IsNullOrEmpty(l.City)) { <span class="ats-xsmall ats-muted">@l.City</span> }
                    </span>
                    <span class="ats-mono ats-xsmall ats-muted">@l.JobCount @(l.JobCount == 1 ? "job" : "jobs")</span>
                    <a class="ms ms-sm ats-faint" asp-controller="Locations" asp-action="Edit" asp-route-id="@l.Id" aria-label="Edit @l.Name">edit</a>
                </div>
            }
            @if (Model.Overview.Locations.Count == 0)
            {
                <partial name="Partials/_EmptyState" model="@(new Ats.Web.Models.Shared.EmptyStateModel("place", "No locations yet."))" />
            }
        </div>
    </div>
</div>
```

- [ ] **Step 4: Verify**

Run: `dotnet build`
Expected: success. Browsing `/Departments` or `/Locations` should now 301 to `/Organisation`
(checked at runtime in Task 8).

*Commit point: `feat: organisation screen; redirect departments and locations`*

---

## Task 4: Restyle Departments/Locations Create-Edit forms

These small forms stay (they are the create/edit path Organisation links to). Restyle to the new card.

**Files:**
- Modify: `src/Ats.Web/Views/Departments/Form.cshtml`
- Modify: `src/Ats.Web/Views/Locations/Form.cshtml`

- [ ] **Step 1: Departments/Form.cshtml**

Read it first to preserve the exact `asp-for` fields and the action/antiforgery shape, then wrap in
the redesign card. It posts to `Create` or `Edit` based on `Model.Id`.

`DepartmentViewModel.Id` is a non-nullable `int` (0 for a new record), so the create/edit switch is
`Model.Id == 0`, matching the original form — not an `is null` check.

```razor
@model Ats.Web.Models.DepartmentViewModel
@{
    ViewData["Title"] = Model.Id == 0 ? "New department" : "Edit department";
    ViewData["Eyebrow"] = "Organisation:";
}
<div class="ats-card" style="max-width:520px">
    <form asp-action="@(Model.Id == 0 ? "Create" : "Edit")" method="post" class="d-flex flex-column gap-3">
        <div asp-validation-summary="All" class="text-danger small"></div>
        <input type="hidden" asp-for="Id" />
        <label class="d-flex flex-column gap-1">
            <span class="form-label mb-0">Name</span>
            <input asp-for="Name" class="form-control" />
            <span asp-validation-for="Name" class="text-danger small"></span>
        </label>
        <div class="d-flex gap-2">
            <button type="submit" class="btn btn-primary">Save</button>
            <a class="btn btn-outline-secondary" asp-controller="Organisation" asp-action="Index">Cancel</a>
        </div>
    </form>
</div>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 2: Locations/Form.cshtml**

Same shape with the Name + City fields (read the current file to confirm both `asp-for`s):

```razor
@model Ats.Web.Models.LocationViewModel
@{
    ViewData["Title"] = Model.Id == 0 ? "New location" : "Edit location";
    ViewData["Eyebrow"] = "Organisation:";
}
<div class="ats-card" style="max-width:520px">
    <form asp-action="@(Model.Id == 0 ? "Create" : "Edit")" method="post" class="d-flex flex-column gap-3">
        <div asp-validation-summary="All" class="text-danger small"></div>
        <input type="hidden" asp-for="Id" />
        <label class="d-flex flex-column gap-1">
            <span class="form-label mb-0">Name</span>
            <input asp-for="Name" class="form-control" />
            <span asp-validation-for="Name" class="text-danger small"></span>
        </label>
        <label class="d-flex flex-column gap-1">
            <span class="form-label mb-0">City</span>
            <input asp-for="City" class="form-control" />
            <span asp-validation-for="City" class="text-danger small"></span>
        </label>
        <div class="d-flex gap-2">
            <button type="submit" class="btn btn-primary">Save</button>
            <a class="btn btn-outline-secondary" asp-controller="Organisation" asp-action="Index">Cancel</a>
        </div>
    </form>
</div>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success. Confirm the model property names (`Id`, `Name`, `City`) by reading
`Ats.Web/Models/DepartmentViewModel.cs` and `LocationViewModel.cs` before finalising; adjust if they
differ.

*Commit point: `feat: restyle department and location forms`*

---

## Task 5: Rebuild the Pipelines screens

**Files:**
- Modify: `src/Ats.Web/Views/Pipelines/Index.cshtml`
- Modify: `src/Ats.Web/Views/Pipelines/Form.cshtml`

- [ ] **Step 1: Rebuild `Pipelines/Index.cshtml`**

Prototype L656-684: template cards showing the name, a "used by N jobs" mono label (the model does
not carry usage counts; render the stage chips instead, which the model does have, and drop the usage
label rather than inventing a number), the stage chips, and edit/delete. The model is
`List<PipelineTemplate>` with `.Stages`.

```razor
@model List<Ats.Domain.Entities.PipelineTemplate>
@using Ats.Domain.Entities
@{
    ViewData["Title"] = "Pipelines";
    ViewData["Eyebrow"] = "Stages, outcomes and referral mapping:";
    string ChipClass(PipelineStage s) => s.IsTerminal
        ? (s.TerminalOutcome == StageOutcome.Hired ? "ats-pill--success" : "ats-pill--danger")
        : "ats-pill--neutral";
}

@section PageActions {
    <a class="btn btn-primary" asp-action="Create"><span class="ms ms-sm">add</span> New pipeline</a>
}

@if (Model.Count == 0)
{
    <partial name="Partials/_EmptyState" model="@(new Ats.Web.Models.Shared.EmptyStateModel("view_week", "No pipelines yet.", "Create one to start moving candidates through stages."))" />
}
else
{
    <div class="row g-3">
        @foreach (var t in Model)
        {
            <div class="col-lg-6">
                <div class="ats-card h-100 d-flex flex-column gap-3">
                    <div class="d-flex align-items-center justify-content-between">
                        <span class="ats-card-title">@t.Name</span>
                        <span class="d-flex gap-2">
                            <a class="ms ms-sm ats-faint" asp-action="Edit" asp-route-id="@t.Id" aria-label="Edit @t.Name">edit</a>
                            <form asp-action="Delete" asp-route-id="@t.Id" method="post" class="d-inline"
                                  onsubmit="return confirm('Delete this pipeline?');">
                                <button type="submit" class="ms ms-sm border-0 bg-transparent p-0" style="color:var(--no-danger-ink)" aria-label="Delete @t.Name">delete</button>
                            </form>
                        </span>
                    </div>
                    <div class="d-flex gap-1 flex-wrap">
                        @foreach (var s in t.Stages.OrderBy(s => s.Order))
                        {
                            <span class="ats-pill @ChipClass(s)" style="gap:0">@s.Name</span>
                        }
                    </div>
                </div>
            </div>
        }
    </div>
}
```

- [ ] **Step 2: Rebuild `Pipelines/Form.cshtml`**

Prototype L686-750: name field, then stage rows in a grid (drag handle placeholder, name, outcome
badge, ReferralTool status input, delete), an add-stage button, and save/cancel with the consequence
note. **Keep the entire `@section Scripts` block byte-for-byte** — the add/remove/reindex JS binds to
`#stages tbody`, `.delete-flag`, `.remove-row`, `#add-stage`, and the `Stages[i]` name pattern; the
markup must keep a `<table id="stages">` with a `<tbody>` and rows whose inputs use
`asp-for="Stages[i].*"`. Restyle the surrounding chrome only.

Read the current `Form.cshtml` in full first. Replace only the outer markup and the table's classes,
preserving every `asp-for`, the hidden `Id`/`Delete`, the `.delete-flag`/`.remove-row` classes, and
the `@section Scripts`. Wrap the form in `<div class="ats-card">`, set the eyebrow/title via
`ViewData`, and change the add-stage/save/cancel buttons to the redesign button classes. Because the
stage-editor JS is intricate and correct, this task is a re-skin, not a rewrite: if in doubt, change
a class attribute, never the element structure or names.

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success. The stage add/remove/reindex still works (checked in Task 8).

*Commit point: `feat: rebuild pipelines screens`*

---

## Task 6: Rebuild the Integrations screens

**Files:**
- Create: `src/Ats.Web/Views/Integration/_DeliveryRows.cshtml`
- Modify: `src/Ats.Web/Views/Integration/Index.cshtml`
- Modify: `src/Ats.Web/Views/Integration/Deliveries.cshtml`
- Modify: `src/Ats.Web/Controllers/IntegrationController.cs`

- [ ] **Step 1: Give the Index a compact delivery preview**

The redesign shows the delivery log inline on the Integrations screen (prototype L831-863). To avoid
duplicating pagination, `Index` shows the most recent page-1 rows with All/Failed/Pending tabs that
link to the full `Deliveries` page. Add a delivery preview to the Index action.

In `IntegrationController.Index`, after building the settings view model, also load the first page of
the log and pass it via `ViewData` (keeps `IntegrationSettingsViewModel` unchanged):

```csharp
        var recent = await _log.SearchAsync(null, 1, 8);
        ViewData["RecentDeliveries"] = recent.Items;
```

`recent.Items` is `IReadOnlyList<DeliveryLogEntry>`.

- [ ] **Step 2: Shared delivery-rows partial**

`src/Ats.Web/Views/Integration/_DeliveryRows.cshtml`, model
`IReadOnlyList<Ats.Application.Integration.DeliveryLogEntry>`. Read `DeliveryLogEntry`'s shape first
(it wraps `Message` (an `OutboxMessage`) and `Deliveries` (a list of `WebhookDelivery`), per the
current `Deliveries.cshtml`). Renders the grid rows used by both Index (preview) and Deliveries (full).

```razor
@using Ats.Domain.Enums
@model IReadOnlyList<Ats.Application.Integration.DeliveryLogEntry>
@{
    const string cols = "grid-template-columns:.6fr 1.2fr 1fr 1fr .9fr 1.8fr;";
    (string Label, Ats.Web.Models.Shared.PillTone Tone) State(OutboxStatus s) => s switch
    {
        OutboxStatus.Delivered => ("Delivered", Ats.Web.Models.Shared.PillTone.Success),
        OutboxStatus.Failed => ("Failed", Ats.Web.Models.Shared.PillTone.Danger),
        _ => ("Pending", Ats.Web.Models.Shared.PillTone.Warning)
    };
}
<div class="ats-thead" style="@cols">
    <span>#</span><span>Candidate ref</span><span>Vacancy</span><span>Status sent</span><span>State</span><span>Last attempt</span>
</div>
@foreach (var e in Model)
{
    var last = e.Deliveries.LastOrDefault();
    var st = State(e.Message.Status);
    <div class="ats-trow @(e.Message.Status == OutboxStatus.Failed ? "ats-trow--danger" : "")" style="@cols">
        <span class="ats-mono ats-xsmall ats-muted">@e.Message.Id</span>
        <code>@e.Message.ExternalCandidateId</code>
        <code>@e.Message.ExternalVacancyId</code>
        <span class="ats-small">@e.Message.CandidateStatus</span>
        <span><partial name="Partials/_StatusPill" model="st == default ? null : new Ats.Web.Models.Shared.StatusPillModel(st.Label, st.Tone)" /></span>
        <span class="ats-cell-stack ats-xsmall ats-muted">
            @if (last is not null) { <span>@last.Kind · HTTP @(last.HttpStatus?.ToString() ?? "-") · @last.AttemptedAt.ToLocalTime().ToString("dd/MM HH:mm") · @e.Message.Attempts attempt(s)</span> }
            @if (!string.IsNullOrEmpty(e.Message.LastError)) { <span style="color:var(--no-danger-ink)">@e.Message.LastError</span> }
        </span>
    </div>
}
@if (Model.Count == 0)
{
    <partial name="Partials/_EmptyState" model="@(new Ats.Web.Models.Shared.EmptyStateModel("cloud_done", "No status updates yet."))" />
}
```

Fix the `_StatusPill` call — it must always pass a model; the `default` guard above is wrong for a
tuple. Use a plain local:

```razor
        <span>
            @{ var pill = new Ats.Web.Models.Shared.StatusPillModel(st.Label, st.Tone); }
            <partial name="Partials/_StatusPill" model="pill" />
        </span>
```

- [ ] **Step 3: Rebuild `Integration/Index.cshtml`**

Prototype L778-829: dark health banner (connection state, customer id, feed pull age, 24h delivered/
failed/pending, Test-connection), the connection form (enable toggle, base URL, customer id, code
param, masked token/key with keep-blank semantics), the feed-key card, and the inline delivery
preview with tabs linking to `Deliveries`. Read the current `Index.cshtml` to preserve every
`asp-for`, the `TempData["FeedKey"]` one-time display, and the three POST forms (save,
GenerateFeedKey, TestConnection).

The masked-secret pattern stays exactly: the inputs are `type="password"` bound to
`ReferralToolAuthToken`/`ReferralToolApiKey` which are blank on load (write-only), and the labels show
"(set; leave blank to keep)" when `HasAuthToken`/`HasApiKey`. Do not render real secret characters.

Provide the full rebuilt view. The dark banner uses `.ats-card-dark`; the feed-pull age reads a new
`ViewData["FeedLastPulledAt"]` the action must set (add
`ViewData["FeedLastPulledAt"] = s.FeedLastPulledAt;` in `Index`, where `s` is the settings). Delivered/
failed/pending counts also come from the log — reuse the recent set is not enough for true 24h counts,
so add three small counts to the action via a new `ViewData` or extend the VM. Simplest without
touching the VM: set `ViewData["Delivered"]`, `ViewData["Failed"]`, `ViewData["Pending"]` from
`IDeliveryLogService.SearchAsync(status, 1, 1).Total` per state:

```csharp
        ViewData["FeedLastPulledAt"] = s.FeedLastPulledAt;
        ViewData["Delivered"] = (await _log.SearchAsync(Ats.Domain.Enums.OutboxStatus.Delivered, 1, 1)).Total;
        ViewData["Failed"] = (await _log.SearchAsync(Ats.Domain.Enums.OutboxStatus.Failed, 1, 1)).Total;
        ViewData["Pending"] = (await _log.SearchAsync(Ats.Domain.Enums.OutboxStatus.Pending, 1, 1)).Total;
```

View:

```razor
@model Ats.Web.Models.IntegrationSettingsViewModel
@using Ats.Application.Common
@{
    ViewData["Title"] = "Integrations";
    ViewData["Eyebrow"] = "Outbound plumbing:";
    var lastPull = ViewData["FeedLastPulledAt"] as DateTimeOffset?;
    var recent = ViewData["RecentDeliveries"] as IReadOnlyList<Ats.Application.Integration.DeliveryLogEntry>
                 ?? new List<Ats.Application.Integration.DeliveryLogEntry>();
}

@if (TempData["FeedKey"] is string newKey)
{
    <div class="alert alert-warning d-flex align-items-start gap-2 mb-3" role="alert">
        <span class="ms ms-sm">key</span>
        <span>New feed API key (copy now, shown once):<br><code>@newKey</code></span>
    </div>
}

<div class="ats-card-dark mb-3 d-flex align-items-center gap-4 flex-wrap">
    <div class="d-flex align-items-center gap-3 flex-grow-1" style="min-width:16rem">
        <span style="width:44px;height:44px;border-radius:var(--no-radius-md);background:rgba(105,202,167,.18);color:var(--no-medium-aqua);display:inline-flex;align-items:center;justify-content:center"><span class="ms">cable</span></span>
        <span class="ats-cell-stack">
            <span style="font-family:var(--no-font-display);font-weight:700;font-size:1.0625rem">ReferralTool</span>
            <span class="ats-small" style="color:#B9C4D0">
                <span class="ats-board-col-dot d-inline-block" style="background:@(Model.IntegrationEnabled ? "var(--no-medium-aqua)" : "var(--no-roman-silver)")"></span>
                @(Model.IntegrationEnabled ? "Connected" : "Disabled")
                @if (Model.ReferralToolCustomerId is not null) { <text>· customer @Model.ReferralToolCustomerId</text> }
                · feed @(lastPull is null ? "never pulled" : RelativeTime.Long(lastPull, DateTimeOffset.UtcNow))
            </span>
        </span>
    </div>
    <div class="ats-stat-strip">
        <span><span class="ats-eyebrow" style="color:#8FA0B4">Delivered</span><span class="ats-stat-mini" style="color:#fff">@ViewData["Delivered"]</span></span>
        <span><span class="ats-eyebrow" style="color:#8FA0B4">Failed</span><span class="ats-stat-mini" style="color:#FF7A96">@ViewData["Failed"]</span></span>
        <span><span class="ats-eyebrow" style="color:#8FA0B4">Pending</span><span class="ats-stat-mini" style="color:#fff">@ViewData["Pending"]</span></span>
    </div>
    <form asp-action="TestConnection" method="post" class="m-0">
        <button type="submit" class="btn btn-outline-light btn-sm"><span class="ms ms-sm">bolt</span> Test connection</button>
    </form>
</div>

<div class="row g-3 align-items-start">
    <div class="col-lg-7">
        <form asp-action="Index" method="post" class="ats-card d-flex flex-column gap-3">
            <div><span class="ats-eyebrow">Connection:</span><h2 class="mt-1 mb-0">Where we push status updates.</h2></div>
            <div asp-validation-summary="All" class="text-danger small"></div>
            <label class="ats-toggle-row d-flex align-items-center gap-3 p-3" style="background:var(--ats-bg);border-radius:var(--no-radius-md);cursor:pointer">
                <input asp-for="IntegrationEnabled" class="form-check-input m-0" />
                <span class="ats-cell-stack">
                    <span class="ats-small">Integration enabled</span>
                    <span class="ats-xsmall ats-muted">Turn off to pause all outbound calls without losing settings.</span>
                </span>
            </label>
            <div class="row g-2">
                <label class="col-12 d-flex flex-column gap-1"><span class="form-label mb-0">Base URL</span><input asp-for="ReferralToolBaseUrl" class="form-control" placeholder="https://api.referraltool.nl" /></label>
                <label class="col-md-6 d-flex flex-column gap-1"><span class="form-label mb-0">Customer id</span><input asp-for="ReferralToolCustomerId" type="number" class="form-control" /></label>
                <label class="col-md-6 d-flex flex-column gap-1"><span class="form-label mb-0">Referral code parameter</span><input asp-for="CodeParameterName" class="form-control" /></label>
                <label class="col-md-6 d-flex flex-column gap-1"><span class="form-label mb-0">X-Auth-Token @(Model.HasAuthToken ? "(set; leave blank to keep)" : "")</span><input asp-for="ReferralToolAuthToken" type="password" autocomplete="off" class="form-control" /></label>
                <label class="col-md-6 d-flex flex-column gap-1"><span class="form-label mb-0">X-Api-Key @(Model.HasApiKey ? "(set; leave blank to keep)" : "")</span><input asp-for="ReferralToolApiKey" type="password" autocomplete="off" class="form-control" /></label>
            </div>
            <div class="border-top pt-3"><button type="submit" class="btn btn-primary">Save settings</button></div>
        </form>
    </div>
    <div class="col-lg-5">
        <div class="ats-card d-flex flex-column gap-3">
            <div><span class="ats-eyebrow">Inbound:</span><h2 class="mt-1 mb-0">Vacancy feed key.</h2></div>
            <p class="ats-small ats-muted mb-0">ReferralTool authenticates with this key to pull your published vacancies. Only the hash is stored.</p>
            <div class="d-flex align-items-center justify-content-between">
                <span class="ats-small">Status</span><strong class="ats-small">@(Model.HasFeedKey ? "configured" : "not set")</strong>
            </div>
            <div class="d-flex align-items-center justify-content-between">
                <span class="ats-small">Published jobs exposed</span><strong class="ats-stat-mini">@Model.PublishedJobCount</strong>
            </div>
            <form asp-action="GenerateFeedKey" method="post"
                  onsubmit="return confirm('Generate a new feed key? The old key stops working immediately.');">
                <button type="submit" class="btn btn-outline-secondary btn-sm">@(Model.HasFeedKey ? "Regenerate" : "Generate") feed key</button>
            </form>
        </div>
    </div>
</div>

<div class="ats-card-flush mt-3">
    <div class="ats-card-head">
        <div><span class="ats-eyebrow">Outbound:</span><h2 class="mt-1 mb-0">Delivery log.</h2></div>
        <div class="ats-filter-group">
            <a class="active" asp-action="Deliveries">All</a>
            <a asp-action="Deliveries" asp-route-status="Failed">Failed</a>
            <a asp-action="Deliveries" asp-route-status="Pending">Pending</a>
        </div>
    </div>
    <partial name="_DeliveryRows" model="recent" />
    <div class="p-3"><a class="ats-small" asp-action="Deliveries">View full delivery log <span class="ms ms-sm">arrow_forward</span></a></div>
</div>

@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 4: Rebuild `Integration/Deliveries.cshtml`**

Full paged log using the shared partial + filter pills + `_Pager`. Read the current file to keep the
`DeliveryLogViewModel`/`PagerModel` construction.

```razor
@model Ats.Web.Models.DeliveryLogViewModel
@using Ats.Domain.Enums
@{
    ViewData["Title"] = "Delivery log";
    ViewData["Eyebrow"] = "Outbound:";
    var cur = Model.Status?.ToString();
    (string T, string? V)[] tabs = { ("All", null), ("Delivered", "Delivered"), ("Failed", "Failed"), ("Pending", "Pending") };
}
@section PageActions {
    <a class="btn btn-outline-secondary" asp-action="Index"><span class="ms ms-sm">arrow_back</span> Integration</a>
}
<div class="ats-filter-group mb-3">
    @foreach (var t in tabs)
    {
        <a class="@((t.V ?? "") == (cur ?? "") ? "active" : "")" asp-action="Deliveries" asp-route-status="@t.V">@t.T</a>
    }
</div>
<div class="ats-card-flush">
    <partial name="_DeliveryRows" model="Model.Results.Items" />
</div>
@{
    var dq = new Dictionary<string, string>();
    if (Model.Status is not null) dq["status"] = Model.Status.ToString()!;
    var pager = new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Deliveries", Query = dq };
}
<partial name="_Pager" model="pager" />
```

- [ ] **Step 5: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild integrations screens`*

---

## Task 7: Audit filtered query + rebuilt screen

**Files:**
- Modify: `src/Ats.Application/Auditing/IAuditLogger.cs` (holds `IAuditQuery`)
- Modify: `src/Ats.Infrastructure/Auditing/AuditQuery.cs`
- Modify: `src/Ats.Web/Controllers/AuditController.cs`
- Create: `src/Ats.Web/Models/AuditIndexViewModel.cs`
- Modify: `src/Ats.Web/Views/Audit/Index.cshtml`

- [ ] **Step 1: Add a filtered/paged query method**

To `IAuditQuery`:

```csharp
    Task<Ats.Application.Common.PagedResult<AuditEntry>> SearchAsync(
        string? q, string? action, DateTimeOffset? from, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default);
```

`RecentAsync` stays (the dashboard uses it).

- [ ] **Step 2: Implement in `AuditQuery`**

```csharp
using Ats.Application.Common;
// existing usings...

    public async Task<PagedResult<AuditEntry>> SearchAsync(string? q, string? action, DateTimeOffset? from, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(a => EF.Functions.Like(a.UserName, $"%{s}%")
                                  || EF.Functions.Like(a.Summary, $"%{s}%")
                                  || (a.EntityRef != null && EF.Functions.Like(a.EntityRef, $"%{s}%")));
        }
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (from is not null) query = query.Where(a => a.OccurredAt >= from);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditEntry>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default) =>
        await _db.AuditEntries.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
```

- [ ] **Step 3: View model + controller**

`src/Ats.Web/Models/AuditIndexViewModel.cs`:

```csharp
using Ats.Application.Common;
using Ats.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class AuditIndexViewModel
{
    public PagedResult<AuditEntry> Results { get; set; } = default!;
    public string? Q { get; set; }
    public string? Action { get; set; }
    public string? Range { get; set; }   // "7" | "30" | null (all)
    public List<SelectListItem> Actions { get; set; } = new();
}
```

`AuditController.Index`:

```csharp
    public async Task<IActionResult> Index(string? q, string? action, string? range, int page = 1)
    {
        if (page < 1) page = 1;
        DateTimeOffset? from = range switch
        {
            "7" => DateTimeOffset.UtcNow.AddDays(-7),
            "30" => DateTimeOffset.UtcNow.AddDays(-30),
            _ => null
        };
        var results = await _audit.SearchAsync(q, action, from, page, 20);
        var actions = (await _audit.DistinctActionsAsync())
            .Select(a => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(a, a, a == action)).ToList();
        return View(new AuditIndexViewModel { Results = results, Q = q, Action = action, Range = range, Actions = actions });
    }
```

- [ ] **Step 4: Rebuild `Audit/Index.cshtml`**

Prototype L867-905: search + action select + date-range buttons, then an icon timeline. Map an action
to an icon/tint by keyword.

```razor
@model Ats.Web.Models.AuditIndexViewModel
@{
    ViewData["Title"] = "Audit log";
    ViewData["Eyebrow"] = "Everything that changed, and who changed it:";
    (string Icon, string Cls) Look(string action)
    {
        var a = action.ToLowerInvariant();
        if (a.Contains("delete")) return ("delete", "ats-pill--danger");
        if (a.Contains("publish")) return ("publish", "ats-pill--success");
        if (a.Contains("move") || a.Contains("stage")) return ("swap_horiz", "ats-pill--info");
        if (a.Contains("create")) return ("person_add", "ats-pill--neutral");
        return ("edit", "ats-pill--neutral");
    }
}
<div class="ats-toolbar mb-3">
    <form asp-action="Index" method="get" class="ats-toolbar" style="gap:.625rem">
        <label class="ats-search" style="width:260px">
            <span class="ms ms-sm ats-muted">search</span>
            <input name="q" value="@Model.Q" placeholder="User, entity or summary" aria-label="Search audit log" />
        </label>
        <select name="action" class="form-select form-select-sm" style="width:auto" onchange="this.form.submit()">
            <option value="">All actions</option>
            @foreach (var a in Model.Actions)
            {
                <option value="@a.Value" selected="@a.Selected">@a.Text</option>
            }
        </select>
        <div class="ats-filter-group">
            <a class="@(Model.Range is null ? "active" : "")" asp-action="Index" asp-route-q="@Model.Q" asp-route-action="@Model.Action">All time</a>
            <a class="@(Model.Range == "7" ? "active" : "")" asp-action="Index" asp-route-range="7" asp-route-q="@Model.Q" asp-route-action="@Model.Action">7 days</a>
            <a class="@(Model.Range == "30" ? "active" : "")" asp-action="Index" asp-route-range="30" asp-route-q="@Model.Q" asp-route-action="@Model.Action">30 days</a>
        </div>
    </form>
</div>

<div class="ats-card">
    @if (Model.Results.Items.Count == 0)
    {
        <partial name="Partials/_EmptyState" model="@(new Ats.Web.Models.Shared.EmptyStateModel("history", "No activity matches."))" />
    }
    else
    {
        <div class="d-flex flex-column">
            @foreach (var a in Model.Results.Items)
            {
                var look = Look(a.Action);
                <div class="d-flex gap-3 py-3" style="border-bottom:1px solid var(--ats-border-subtle)">
                    <span class="ats-mono ats-xsmall ats-muted" style="width:6.25rem;flex:0 0 6.25rem;padding-top:.2rem">@a.OccurredAt.ToLocalTime().ToString("dd/MM HH:mm")</span>
                    <span class="ats-pill @look.Cls" style="width:2.125rem;height:2.125rem;padding:0;justify-content:center;border-radius:var(--no-radius-pill);flex:0 0 2.125rem"><span class="ms ms-sm">@look.Icon</span></span>
                    <span class="ats-cell-stack flex-grow-1">
                        <span class="ats-small"><strong style="font-weight:500">@a.UserName</strong> @a.Summary</span>
                        <span class="ats-mono ats-xsmall ats-faint">@a.EntityType@(string.IsNullOrEmpty(a.EntityRef) ? "" : " · " + a.EntityRef)</span>
                    </span>
                    <span class="ats-pill ats-pill--neutral" style="height:fit-content">@a.Action</span>
                </div>
            }
        </div>
    }
</div>

@{
    var aq = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(Model.Q)) aq["q"] = Model.Q;
    if (!string.IsNullOrEmpty(Model.Action)) aq["action"] = Model.Action;
    if (!string.IsNullOrEmpty(Model.Range)) aq["range"] = Model.Range;
    var pager = new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Index", Query = aq };
}
<partial name="_Pager" model="pager" />
```

- [ ] **Step 5: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild audit log with filters and paging`*

---

## Task 8: Career-site back office + branding editor

**Files:**
- Create: `src/Ats.Web/Controllers/CareerSiteController.cs`
- Create: `src/Ats.Web/Models/BrandingEditViewModel.cs`
- Create: `src/Ats.Web/Views/CareerSite/Index.cshtml`
- Create: `src/Ats.Web/Views/CareerSite/Branding.cshtml`

- [ ] **Step 1: Branding form model**

`src/Ats.Web/Models/BrandingEditViewModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class BrandingEditViewModel
{
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a 6-digit hex colour like #0085CA.")]
    public string? AccentColor { get; set; }
    public SidebarTheme SidebarTheme { get; set; } = SidebarTheme.Dark;
    [StringLength(160)] public string? CareerHeroHeadline { get; set; }
    [StringLength(160)] public string? CareerHeroHeadlineOutlined { get; set; }
    [StringLength(600)] public string? CareerHeroIntro { get; set; }

    public string TenantName { get; set; } = "";
    public string TenantSlug { get; set; } = "";
}
```

- [ ] **Step 2: Controller**

`src/Ats.Web/Controllers/CareerSiteController.cs`. Index is for any authenticated user; Branding is
Owner-only (matching Integration/Audit). Reuses `ITenantBrandingService` from Phase 1.

```csharp
using Ats.Application.Branding;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class CareerSiteController : Controller
{
    private readonly ITenantBrandingService _branding;
    public CareerSiteController(ITenantBrandingService branding) => _branding = branding;

    public async Task<IActionResult> Index()
    {
        var b = await _branding.GetAsync();
        return View(b);   // model: TenantBranding
    }

    [HttpGet]
    [Authorize(Roles = AtsRole.Owner)]
    public async Task<IActionResult> Branding()
    {
        var b = await _branding.GetAsync();
        return View(new BrandingEditViewModel
        {
            AccentColor = b.Accent,
            SidebarTheme = b.SidebarTheme,
            CareerHeroHeadline = b.CareerHeroHeadline,
            CareerHeroHeadlineOutlined = b.CareerHeroHeadlineOutlined,
            CareerHeroIntro = b.CareerHeroIntro,
            TenantName = b.TenantName,
            TenantSlug = b.TenantSlug
        });
    }

    [HttpPost]
    [Authorize(Roles = AtsRole.Owner)]
    public async Task<IActionResult> Branding(BrandingEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _branding.UpdateAsync(new BrandingInput(
            vm.AccentColor, vm.SidebarTheme, vm.CareerHeroHeadline, vm.CareerHeroHeadlineOutlined, vm.CareerHeroIntro));
        TempData["Success"] = "Branding saved.";
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 3: Index view (browser-frame preview)**

`src/Ats.Web/Views/CareerSite/Index.cshtml`, model `Ats.Application.Branding.TenantBranding`.
Prototype L907-968: a browser frame showing the public URL, an Oxford-Blue hero preview using the
tenant's hero copy (or defaults), and Branding + open-live-site actions. The live URL is the public
careers route for this tenant's slug.

```razor
@using Ats.Domain.Enums
@model Ats.Application.Branding.TenantBranding
@{
    ViewData["Title"] = "Career site";
    ViewData["Eyebrow"] = $"/careers/{Model.TenantSlug}:";
    var url = Url.Action("Index", "Jobs", new { area = "Careers", slug = Model.TenantSlug });
    var headline = string.IsNullOrWhiteSpace(Model.CareerHeroHeadline) ? "Build things that" : Model.CareerHeroHeadline;
    var outlined = string.IsNullOrWhiteSpace(Model.CareerHeroHeadlineOutlined) ? "actually ship." : Model.CareerHeroHeadlineOutlined;
    var intro = string.IsNullOrWhiteSpace(Model.CareerHeroIntro) ? "Open roles, one team, no noise." : Model.CareerHeroIntro;
}

@section PageActions {
    @if (User.IsInRole(AtsRole.Owner))
    {
        <a class="btn btn-outline-secondary" asp-action="Branding"><span class="ms ms-sm">palette</span> Branding</a>
    }
    <a class="btn btn-primary" href="@url" target="_blank" rel="noopener"><span class="ms ms-sm">open_in_new</span> Open live site</a>
}

<div class="ats-browser-frame">
    <div class="ats-browser-chrome">
        <span class="ats-browser-dot"></span><span class="ats-browser-dot"></span><span class="ats-browser-dot"></span>
        <span class="ats-browser-url">@(Context.Request.Host.Value)@url</span>
    </div>
    <div style="background:var(--no-oxford-blue);padding:3.5rem 4rem;position:relative;overflow:hidden">
        <div style="position:absolute;width:520px;height:420px;border-radius:50%;background:var(--ats-accent);opacity:.34;filter:blur(10px);right:-120px;top:-140px"></div>
        <div style="position:absolute;width:380px;height:320px;border-radius:50%;background:var(--no-medium-aqua);opacity:.26;filter:blur(10px);right:120px;bottom:-180px"></div>
        <div style="position:relative;max-width:640px;display:flex;flex-direction:column;gap:1rem">
            <span class="ats-eyebrow" style="color:#8FA0B4">@Model.TenantName · careers</span>
            <h2 style="font-family:var(--no-font-display);font-weight:800;font-size:3rem;line-height:1.12;letter-spacing:-.01em;margin:0;color:#fff">@headline<br /><span style="color:transparent;-webkit-text-stroke:1.5px #fff">@outlined</span></h2>
            <p style="font-size:1.0625rem;font-weight:300;line-height:1.56;color:#C6D0DA;margin:0">@intro</p>
        </div>
    </div>
</div>
```

- [ ] **Step 4: Branding editor view**

`src/Ats.Web/Views/CareerSite/Branding.cshtml`, model `BrandingEditViewModel`.

```razor
@model Ats.Web.Models.BrandingEditViewModel
@using Ats.Domain.Enums
@{
    ViewData["Title"] = "Branding";
    ViewData["Eyebrow"] = "Career site:";
}
@section PageActions {
    <a class="btn btn-outline-secondary" asp-action="Index"><span class="ms ms-sm">arrow_back</span> Career site</a>
}
<form asp-action="Branding" method="post" class="ats-card d-flex flex-column gap-3" style="max-width:640px">
    <div asp-validation-summary="All" class="text-danger small"></div>
    <label class="d-flex flex-column gap-1" style="max-width:12rem">
        <span class="form-label mb-0">Accent colour</span>
        <input asp-for="AccentColor" type="color" class="form-control form-control-color" style="width:4rem;height:2.4rem" />
        <span asp-validation-for="AccentColor" class="text-danger small"></span>
    </label>
    <label class="d-flex flex-column gap-1" style="max-width:16rem">
        <span class="form-label mb-0">Sidebar theme</span>
        <select asp-for="SidebarTheme" asp-items="Html.GetEnumSelectList<SidebarTheme>()" class="form-select"></select>
    </label>
    <label class="d-flex flex-column gap-1">
        <span class="form-label mb-0">Career hero headline</span>
        <input asp-for="CareerHeroHeadline" class="form-control" placeholder="Build things that" />
    </label>
    <label class="d-flex flex-column gap-1">
        <span class="form-label mb-0">Outlined second line</span>
        <input asp-for="CareerHeroHeadlineOutlined" class="form-control" placeholder="actually ship." />
    </label>
    <label class="d-flex flex-column gap-1">
        <span class="form-label mb-0">Hero intro</span>
        <textarea asp-for="CareerHeroIntro" class="form-control" rows="3"></textarea>
    </label>
    <div class="d-flex gap-2 border-top pt-3">
        <button type="submit" class="btn btn-primary">Save branding</button>
        <a class="btn btn-outline-secondary" asp-action="Index">Cancel</a>
    </div>
</form>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

Note the accent uses `<input type="color">`, which always posts a valid `#rrggbb`, so the regex
validator and `BrandColor.Normalize` will accept it; the server-side validation from Phase 1 remains
the real guard.

- [ ] **Step 5: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: career-site back office and branding editor`*

---

## Task 9: Verify end to end (developer, signed in)

- [ ] **Step 1: Build + tests**

`dotnet build`; `dotnet test tests/Ats.Tests/Ats.Tests.csproj` — all green (Phase 2's 63 + Task 1's 5).

- [ ] **Step 2: Walk the screens** (signed in, desktop width)

1. **Sidebar** — Organisation and Career site links now resolve (no longer the styled 404).
2. **Pipelines** — template cards with stage chips (Hired green, Rejected red); open the editor, add a
   stage, remove a stage, reorder, save — the stage-editor JS still works and the save persists.
3. **Organisation** — departments and locations with correct job counts; Add opens the create form;
   Edit opens the edit form; both save and return to `/Organisation`.
4. **Old routes** — browse `/Departments` and `/Locations`; both 301-redirect to `/Organisation`.
5. **Integrations** — dark health banner with connection state, customer id, feed-pull age (now a real
   relative time once the feed has been pulled — see step 6), and 24h delivered/failed/pending; save
   settings (secrets left blank stay set); regenerate feed key shows the one-time key; test connection;
   inline delivery log with tabs; "View full delivery log" opens the paged Deliveries with filters.
6. **Feed pull timestamp** — call the feed endpoint with a valid key
   (`POST /jobs/search` with `Authorization: Token <key>` against `Ats.Api`), reload Integrations, and
   confirm the banner and the dashboard card now show "pulled N seconds/minutes ago"; call it again
   immediately and confirm the timestamp does not advance more than once a minute (throttle).
7. **Audit** — search, action filter, and 7/30-day range all narrow the list; icon timeline renders;
   pager works.
8. **Career site** — the browser-frame preview shows the hero with default copy; as Owner, Branding
   opens; change the accent colour and sidebar theme and hero copy, save; confirm the shell accent and
   sidebar theme change (branding pipeline from Phase 1) and the preview hero reflects the new copy.
9. **Non-Owner** — sign in as a Recruiter: Career site is visible but the Branding button is hidden and
   `/CareerSite/Branding` is refused; Integrations and Audit remain hidden.

- [ ] **Step 3: Confirm no regressions**

Publish/close still drive the public career site; the delivery log and feed key behaviour are
unchanged; the ReferralTool contract is untouched.

*Commit point: `test: verify phase 3 screens, redirects, feed timestamp and branding`*

---

## Task 10: Documentation

**Files:**
- Modify: `.claude/skills/ui/SKILL.md`, `.claude/skills/integration/SKILL.md`,
  `.claude/skills/audit/SKILL.md`, `.claude/skills/career-site/SKILL.md`, `CLAUDE.md`

- [ ] **Step 1: Record the changes**

- `ui`: Organisation merges Departments + Locations (old Index routes 301 to `/Organisation`);
  new `CareerSite` controller (Index preview + Owner-only Branding) reuses `ITenantBrandingService`;
  the browser-frame preview classes.
- `integration`: `FeedLastPulledAt` is now written by `Ats.Api`'s `FeedController` via
  `IVacancyFeedRepository.TouchFeedPulledAsync`, debounced by `FeedPullThrottle` (once/minute) and
  failure-swallowed; the Integrations screen shows the health banner + inline delivery log.
- `audit`: `IAuditQuery.SearchAsync` (filtered + paged) backs the rebuilt audit screen; `RecentAsync`
  still backs the dashboard feed.
- `career-site`: the back-office `CareerSite` screen and branding editor; the public hero now reads
  `CareerHeroHeadline`/`Outlined`/`Intro` from `TenantSettings` (Phase 4 renders them publicly).
- `CLAUDE.md`: note `/Organisation` and `/CareerSite` in the Ats.Web row if routes are listed.

- [ ] **Step 2: Verify** — re-read each edited section against the built code.

*Commit point: `docs: update skills for phase 3`*

---

## Phase 3 exit criteria

- [ ] `dotnet build` clean; `dotnet test` green (68 tests).
- [ ] Pipelines, Organisation, Integrations, Audit rebuilt; Career site back office + branding editor live.
- [ ] `/Departments` and `/Locations` 301-redirect to `/Organisation`; create/edit/delete still work.
- [ ] Feed-pull timestamp advances at most once a minute and surfaces on the dashboard + integrations.
- [ ] Branding editor changes the shell accent + sidebar theme and the career-site hero copy; the
      accent is validated server-side.
- [ ] Owner-gating holds: non-Owner cannot reach Branding, Integrations, or Audit.
- [ ] No new migration; ReferralTool contract, outbox and worker untouched.

Deferred to Phase 4: the public career site itself (hero with blobs + outlined headline, role cards,
apply form, thank-you) — the last redesign surface.
