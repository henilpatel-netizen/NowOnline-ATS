# ATS NowOnline Redesign - Phase 2: Recruiting screens + read models

> **For agentic workers:** checkbox (`- [ ]`) steps, worked in order.
>
> **Same two repo constraints as Phase 1:** no `git` commands (each task ends with a Verify step;
> commit points are marked for the developer), and no `dotnet ef database update`. **Phase 2 adds no
> migration** — every column it needs already shipped in Phase 1's `AddBrandingAndApplicationOrigin`.
>
> **Visual source of truth:** `ATS - Redesign.dc.html` in the handoff bundle. Screen line refs:
> dashboard L126-270, jobs L273-389, board L392-579, candidates L582-644, drawer L974-1041.
> Component classes for all of this already exist in `wwwroot/css/ats-components.css` (Phase 1).

**Goal:** Rebuild the five recruiting screens — dashboard, jobs list, board, candidates, and the
candidate drawer — into the prototype's layouts, backed by read models that compute the metrics the
design shows (time-to-hire, offer acceptance, source split, per-stage counts, days-in-stage,
delivery state) from existing tables. Stamp `ApplicationOrigin` at creation so the source chips are real.

**Architecture:** Read models live in `Ats.Application/<area>` as records + service interfaces,
implemented with EF projections in `Ats.Infrastructure/<area>`. Pure calculations (means, rates,
percentage splits) are extracted into testable static helpers in `Ats.Application/Common` and unit
tested; the EF projections and Razor are verified by build + a signed-in browser walkthrough (there
is no test database). Controllers stay thin. The board keeps its exact SortableJS + htmx + RowVersion
move flow — only its markup and the card model change.

**Tech Stack:** unchanged from Phase 1.

**Spec:** `docs/specs/2026-07-30-ats-nowonline-redesign-design.md`
**Prior phase:** `docs/plans/2026-07-30-ats-redesign-phase-1-foundation.md` (committed).

**Origin stamping (backend behaviour added this phase).** Two creation sites, both presentation-only
writes that never touch the integration path:
- `CareerService.ApplyAsync` (`Ats.Application/Career`) — `Origin = SourceCode present ? Referral : CareerSite`.
- `ApplicationService.CreateApplicationAsync` (`Ats.Application/Applications`) — `Origin = Manual`
  (covers board "Add candidate" and candidates "Add to job", the only callers).
Rows created before this phase keep `Unknown`, rendered as "Not recorded".

---

## File Structure

### Created

| File | Responsibility |
|---|---|
| `tests/Ats.Tests/Dashboard/DashboardMathTests.cs` | mean-days, rate, percentage-split |
| `src/Ats.Application/Common/DashboardMath.cs` | pure metric calculations |
| `src/Ats.Application/Dashboard/DashboardSummary.cs` | **replaced** — richer summary record |
| `src/Ats.Application/Jobs/JobListItem.cs` | jobs-list projection record + `IJobListQuery` |
| `src/Ats.Application/Candidates/CandidateListItem.cs` | candidates-list projection record + `ICandidateListQuery` |
| `src/Ats.Application/Applications/ApplicationCard.cs` | drawer/detail projection record + `IApplicationCardQuery` |
| `src/Ats.Infrastructure/Jobs/JobListQuery.cs` | EF projection |
| `src/Ats.Infrastructure/Candidates/CandidateListQuery.cs` | EF projection |
| `src/Ats.Infrastructure/Applications/ApplicationCardQuery.cs` | EF projection |
| `src/Ats.Web/Views/Shared/Partials/_CandidateDrawer.cshtml` | the 520px drawer body |
| `src/Ats.Web/Models/Board/BoardCardModel.cs` | richer board card view data |

### Modified

| File | Change |
|---|---|
| `src/Ats.Application/Abstractions/IFileStore.cs` | add `StatAsync` + `StoredFileInfo` |
| `src/Ats.Infrastructure/Files/LocalFileStore.cs` | implement `StatAsync` |
| `src/Ats.Application/Career/CareerService.cs` | stamp `Origin` |
| `src/Ats.Application/Applications/ApplicationService.cs` | stamp `Origin` |
| `src/Ats.Infrastructure/Dashboard/DashboardService.cs` | build the richer summary |
| `src/Ats.Infrastructure/DependencyInjection.cs` | register 3 query services |
| `src/Ats.Web/Controllers/DashboardController.cs` | (unchanged signature; richer model) |
| `src/Ats.Web/Controllers/JobsController.cs` | Index uses `IJobListQuery` |
| `src/Ats.Web/Controllers/CandidatesController.cs` | Index uses `ICandidateListQuery` |
| `src/Ats.Web/Controllers/BoardController.cs` | build richer cards |
| `src/Ats.Web/Controllers/ApplicationsController.cs` | add `Card` action; Details reuses drawer |
| `src/Ats.Web/Models/JobsIndexViewModel.cs` | hold `PagedResult<JobListItem>` |
| `src/Ats.Web/Models/CandidatesIndexViewModel.cs` | hold `PagedResult<CandidateListItem>` |
| `src/Ats.Web/Models/BoardViewModel.cs` | columns carry `BoardCardModel` + stage meta |
| `src/Ats.Web/Models/ApplicationDetailsViewModel.cs` | carry an `ApplicationCard` |
| `src/Ats.Web/Views/Dashboard/Index.cshtml` | rebuilt |
| `src/Ats.Web/Views/Jobs/Index.cshtml` | rebuilt |
| `src/Ats.Web/Views/Board/Index.cshtml` | rebuilt (header, tabs, stats strip) |
| `src/Ats.Web/Views/Board/_Board.cshtml` | rebuilt (kanban columns + rich cards) |
| `src/Ats.Web/Views/Candidates/Index.cshtml` | rebuilt |
| `src/Ats.Web/Views/Applications/Details.cshtml` | render `_CandidateDrawer` |
| `src/Ats.Web/wwwroot/js/site.js` | drawer open/close wiring |

---

## Task 1: Dashboard metric math (TDD)

**Files:**
- Create: `tests/Ats.Tests/Dashboard/DashboardMathTests.cs`
- Create: `src/Ats.Application/Common/DashboardMath.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Dashboard;

public class DashboardMathTests
{
    [Fact]
    public void MeanDays_averages_span_lengths()
    {
        var spans = new[] { TimeSpan.FromDays(10), TimeSpan.FromDays(20), TimeSpan.FromDays(30) };
        Assert.Equal(20, DashboardMath.MeanDays(spans));
    }

    [Fact]
    public void MeanDays_rounds_to_nearest_whole_day()
    {
        var spans = new[] { TimeSpan.FromDays(10), TimeSpan.FromDays(11) }; // 10.5 -> 11 (banker's-safe)
        Assert.Equal(11, DashboardMath.MeanDays(spans));
    }

    [Fact]
    public void MeanDays_is_null_for_an_empty_set()
    {
        Assert.Null(DashboardMath.MeanDays(Array.Empty<TimeSpan>()));
    }

    [Fact]
    public void MeanDays_treats_negative_spans_as_zero()
    {
        // A clock-skewed event that lands before AppliedAt must not drag the mean negative.
        var spans = new[] { TimeSpan.FromDays(-5), TimeSpan.FromDays(10) };
        Assert.Equal(5, DashboardMath.MeanDays(spans));
    }

    [Theory]
    [InlineData(7, 9, 78)]     // 7/9 = 0.777... -> 78%
    [InlineData(1, 1, 100)]
    [InlineData(0, 5, 0)]
    public void Percent_rounds_a_ratio(int numerator, int denominator, int expected)
    {
        Assert.Equal(expected, DashboardMath.Percent(numerator, denominator));
    }

    [Fact]
    public void Percent_is_null_when_the_denominator_is_zero()
    {
        Assert.Null(DashboardMath.Percent(3, 0));
    }

    [Fact]
    public void Split_returns_percentages_that_sum_to_100()
    {
        // 61/27/12 style split from three raw counts.
        var split = DashboardMath.Split(new[] { 61, 27, 12 });
        Assert.Equal(100, split.Sum());
    }

    [Fact]
    public void Split_absorbs_rounding_drift_into_the_largest_bucket()
    {
        // 1/1/1 -> 33/33/34 (or 34/33/33): must still total 100, largest bucket takes the remainder.
        var split = DashboardMath.Split(new[] { 1, 1, 1 });
        Assert.Equal(100, split.Sum());
        Assert.Equal(34, split.Max());
    }

    [Fact]
    public void Split_of_all_zero_is_all_zero()
    {
        Assert.All(DashboardMath.Split(new[] { 0, 0, 0 }), p => Assert.Equal(0, p));
    }
}
```

- [ ] **Step 2: Run - expect compile failure**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: `DashboardMath` does not exist.

- [ ] **Step 3: Implement**

`src/Ats.Application/Common/DashboardMath.cs`:

```csharp
namespace Ats.Application.Common;

public static class DashboardMath
{
    // Mean of the spans in whole days, negatives clamped to zero. Null for an empty set.
    public static int? MeanDays(IReadOnlyCollection<TimeSpan> spans)
    {
        if (spans.Count == 0) return null;
        var avg = spans.Average(s => Math.Max(0, s.TotalDays));
        return (int)Math.Round(avg, MidpointRounding.AwayFromZero);
    }

    // Percentage numerator/denominator, rounded. Null when the denominator is zero.
    public static int? Percent(int numerator, int denominator)
    {
        if (denominator == 0) return null;
        return (int)Math.Round(numerator * 100.0 / denominator, MidpointRounding.AwayFromZero);
    }

    // Whole-percent split of counts that always totals 100 (0 when everything is zero).
    // Rounding drift is absorbed by the largest bucket so bars never over/undershoot.
    public static int[] Split(IReadOnlyList<int> counts)
    {
        var total = counts.Sum();
        if (total == 0) return counts.Select(_ => 0).ToArray();

        var raw = counts.Select(c => c * 100.0 / total).ToArray();
        var floored = raw.Select(r => (int)Math.Floor(r)).ToArray();
        var remainder = 100 - floored.Sum();

        // Hand the leftover points to the buckets with the largest fractional parts.
        var order = Enumerable.Range(0, counts.Count)
            .OrderByDescending(i => raw[i] - floored[i])
            .ToArray();
        for (var k = 0; k < remainder; k++) floored[order[k % order.Length]]++;
        return floored;
    }
}
```

- [ ] **Step 4: Run - expect pass**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: all pass (Phase 1's 52 plus these).

If `Split_absorbs_rounding_drift_into_the_largest_bucket` expects 34 as the max but the algorithm
gives the point to a different equal-fraction bucket, that's fine mathematically but fails the exact
assertion; the tie-break above orders by fractional part then by original index, so the first bucket
wins the point and `Max()` is 34. Do not change the assertion; confirm the implementation matches.

*Commit point: `feat: dashboard metric math helpers`*

---

## Task 2: IFileStore.StatAsync

**Files:**
- Modify: `src/Ats.Application/Abstractions/IFileStore.cs`
- Modify: `src/Ats.Infrastructure/Files/LocalFileStore.cs`

- [ ] **Step 1: Add the contract**

Append to `IFileStore.cs` (keep everything already there):

```csharp
public sealed record StoredFileInfo(long Length, string ContentType, string FileName);
```

and inside the `IFileStore` interface:

```csharp
    // Metadata for a stored file, or null if the key is invalid or missing. Does not open a stream.
    Task<StoredFileInfo?> StatAsync(string key, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `LocalFileStore`**

The key-validation and content-type logic must match `OpenAsync` exactly. Factor the shared bits
rather than duplicating: add a private guard and a private content-type map, and have both methods
use them.

Add these private members and the new method:

```csharp
    private static bool IsBareKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && !key.Contains('/') && !key.Contains('\\') && !key.Contains("..");

    private static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    public Task<StoredFileInfo?> StatAsync(string key, CancellationToken ct = default)
    {
        if (!IsBareKey(key)) return Task.FromResult<StoredFileInfo?>(null);
        var path = Path.Combine(_root, key);
        if (!File.Exists(path)) return Task.FromResult<StoredFileInfo?>(null);
        var info = new FileInfo(path);
        return Task.FromResult<StoredFileInfo?>(
            new StoredFileInfo(info.Length, ContentTypeFor(key), "resume" + Path.GetExtension(key)));
    }
```

Then refactor `OpenAsync` and `DeleteAsync` to call `IsBareKey`, and `OpenAsync` to call
`ContentTypeFor`, so the three methods cannot drift apart. Do not change their behaviour.

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success (the Worker also references `IFileStore` transitively; a missing member would fail
its build too).

*Commit point: `feat: add IFileStore.StatAsync`*

---

## Task 3: Stamp ApplicationOrigin at creation

**Files:**
- Modify: `src/Ats.Application/Applications/ApplicationService.cs`
- Modify: `src/Ats.Application/Career/CareerService.cs`

- [ ] **Step 1: Manual path (board + candidates) → Manual**

In `ApplicationService.CreateApplicationAsync`, the `new JobApplication { ... }` initializer sets
`CandidateId`, `JobId`, `CurrentStageId`, `AppliedAt`, `Status`. Add:

```csharp
            Origin = ApplicationOrigin.Manual,
```

Add `using Ats.Domain.Enums;` if not already present (it is — the file uses `ApplicationStatus`).

- [ ] **Step 2: Career path → Referral or CareerSite**

In `CareerService.ApplyAsync`, the `new JobApplication { ... }` initializer sets `SourceCode = code`.
Add, right after it:

```csharp
            Origin = code is not null ? ApplicationOrigin.Referral : ApplicationOrigin.CareerSite,
```

`code` is the trimmed-or-null local already computed above (`var code = ...`). Add
`using Ats.Domain.Enums;` to the file if the enum is not in scope.

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success.

This is a behaviour change with no schema change and no effect on the outbox/feed/worker, which never
read `Origin`. Runtime confirmation happens in Task 10 (apply via the career site → the board card
shows a "Career site"/"Referral" chip; add via the board → "Manual").

*Commit point: `feat: stamp application origin at creation`*

---

## Task 4: Jobs list projection

**Files:**
- Create: `src/Ats.Application/Jobs/JobListItem.cs`
- Create: `src/Ats.Infrastructure/Jobs/JobListQuery.cs`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Define the projection + query contract**

`src/Ats.Application/Jobs/JobListItem.cs`:

```csharp
using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

// A stage bucket for the mini pipeline bar: stage name + active-application count, in stage order.
public sealed record JobStageCount(string Stage, int Count);

public sealed record JobListItem(
    int Id,
    string Title,
    string ExternalRef,
    JobStatus Status,
    string? Department,
    string? Location,
    DateTimeOffset? PublishedAt,
    int TotalApplications,
    int ActiveApplications,
    IReadOnlyList<JobStageCount> StageCounts,
    IReadOnlyList<string> TopApplicantNames);

public interface IJobListQuery
{
    Task<PagedResult<JobListItem>> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement the EF projection**

`src/Ats.Infrastructure/Jobs/JobListQuery.cs`. Filter/paginate on the server; compute counts and the
avatar-stack names per returned page (bounded, so N is at most `pageSize` jobs).

```csharp
using Ats.Application.Common;
using Ats.Application.Jobs;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Jobs;

public sealed class JobListQuery : IJobListQuery
{
    private readonly AtsDbContext _db;
    public JobListQuery(AtsDbContext db) => _db = db;

    public async Task<PagedResult<JobListItem>> SearchAsync(JobStatus? status, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Jobs.AsQueryable();
        if (status is not null) q = q.Where(j => j.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(j => EF.Functions.Like(j.Title, $"%{s}%") || EF.Functions.Like(j.ExternalRef, $"%{s}%"));
        }

        var total = await q.CountAsync(ct);

        var jobs = await q
            .OrderByDescending(j => j.PublishedAt).ThenByDescending(j => j.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new
            {
                j.Id, j.Title, j.ExternalRef, j.Status, j.PublishedAt,
                Department = j.Department != null ? j.Department.Name : null,
                Location = j.Location != null ? (j.Location.City ?? j.Location.Name) : null
            })
            .ToListAsync(ct);

        var ids = jobs.Select(j => j.Id).ToList();

        // Active applications for the visible jobs, joined to stage name + order, grouped in memory.
        var apps = await (
            from a in _db.Applications
            where ids.Contains(a.JobId) && a.Status == ApplicationStatus.Active
            join st in _db.PipelineStages on a.CurrentStageId equals st.Id
            select new { a.JobId, a.CandidateId, StageName = st.Name, st.Order })
            .ToListAsync(ct);

        var totalByJob = await _db.Applications
            .Where(a => ids.Contains(a.JobId))
            .GroupBy(a => a.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobId, x => x.Count, ct);

        // First three applicant names per visible job for the avatar stack.
        var names = (await (
            from a in _db.Applications
            where ids.Contains(a.JobId)
            join c in _db.Candidates on a.CandidateId equals c.Id
            select new { a.JobId, a.Id, Name = c.FirstName + " " + c.LastName })
            .ToListAsync(ct))
            .GroupBy(x => x.JobId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).Select(x => x.Name).Take(3).ToList());

        var byJob = apps.GroupBy(a => a.JobId).ToDictionary(g => g.Key, g => g.ToList());

        var items = jobs.Select(j =>
        {
            var jobApps = byJob.TryGetValue(j.Id, out var list) ? list : new();
            var stageCounts = jobApps
                .GroupBy(a => new { a.StageName, a.Order })
                .OrderBy(g => g.Key.Order)
                .Select(g => new JobStageCount(g.Key.StageName, g.Count()))
                .ToList();
            return new JobListItem(
                j.Id, j.Title, j.ExternalRef, j.Status, j.Department, j.Location, j.PublishedAt,
                totalByJob.TryGetValue(j.Id, out var t) ? t : 0,
                jobApps.Count,
                stageCounts,
                names.TryGetValue(j.Id, out var n) ? n : new List<string>());
        }).ToList();

        return new PagedResult<JobListItem>(items, page, pageSize, total);
    }
}
```

- [ ] **Step 3: Register it**

In `DependencyInjection.cs` add the usings (`Ats.Application.Jobs` is already imported;
add `using Ats.Infrastructure.Jobs;`) and:

```csharp
        services.AddScoped<IJobListQuery, JobListQuery>();
```

- [ ] **Step 4: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: jobs list projection with stage counts and applicants`*

---

## Task 5: Rebuild the Jobs list view

**Files:**
- Modify: `src/Ats.Web/Models/JobsIndexViewModel.cs`
- Modify: `src/Ats.Web/Controllers/JobsController.cs`
- Modify: `src/Ats.Web/Views/Jobs/Index.cshtml`

- [ ] **Step 1: Point the view model at the projection**

`JobsIndexViewModel.cs`:

```csharp
using Ats.Application.Common;
using Ats.Application.Jobs;
using Ats.Domain.Enums;

namespace Ats.Web.Models;

public class JobsIndexViewModel
{
    public PagedResult<JobListItem> Results { get; set; } = default!;
    public string? Q { get; set; }
    public JobStatus? Status { get; set; }
}
```

- [ ] **Step 2: Use the query in the controller**

In `JobsController`, inject `IJobListQuery` (add a constructor parameter `IJobListQuery jobList` and a
field) and change `Index` to call it. Leave every other action untouched.

```csharp
    public async Task<IActionResult> Index(string? q, Ats.Domain.Enums.JobStatus? status, int page = 1)
    {
        if (page < 1) page = 1;
        var results = await _jobList.SearchAsync(status, q, page, 20);
        return View(new JobsIndexViewModel { Results = results, Q = q, Status = status });
    }
```

Set `ViewData["Eyebrow"]` from the status counts and a topbar action in the view (Step 3).

- [ ] **Step 3: Rebuild `Views/Jobs/Index.cshtml`**

Prototype L273-389. Filter pills (All/Published/Draft/Closed as GET links preserving `q`), a search
box, and a card-flush table using `.ats-thead`/`.ats-trow` grid rows with: title + ref/department/
location subline, a `_StatusPill`, the `_PipelineBar` (ShowLabels) built from `StageCounts`, an
avatar stack from `TopApplicantNames` with a `+N` overflow, published date, and a `more_horiz` action
cell. Keep the existing per-row actions (Board/Edit/Publish/Close/Delete) reachable — put them behind
the `more_horiz` as a Bootstrap dropdown so no capability is lost. Keep the `_Pager` partial and its
`PagerModel` construction exactly as today (page size 20, preserving `q`/`status`).

Full view:

```razor
@model Ats.Web.Models.JobsIndexViewModel
@using Ats.Domain.Enums
@using Ats.Web.Models.Shared
@{
    ViewData["Title"] = "Jobs";
    var published = Model.Results.Items.Count(j => j.Status == JobStatus.Published);
    ViewData["Eyebrow"] = $"{Model.Results.Total} total:";

    PillTone Tone(JobStatus s) => s switch
    {
        JobStatus.Published => PillTone.Success,
        JobStatus.Closed => PillTone.Neutral,
        _ => PillTone.Warning
    };

    (string Text, string? Val)[] filters =
    {
        ("All", null), ("Published", nameof(JobStatus.Published)),
        ("Draft", nameof(JobStatus.Draft)), ("Closed", nameof(JobStatus.Closed))
    };
    var current = Model.Status?.ToString();
    const string cols = "grid-template-columns:2.4fr 1fr 1.6fr 1fr .8fr 44px;";
}

@section PageActions {
    <a class="btn btn-primary" asp-action="Create"><span class="ms ms-sm">add</span> New job</a>
}

<div class="ats-toolbar mb-3">
    <form asp-action="Index" method="get" class="ats-search" style="width:280px" role="search">
        @if (Model.Status is not null) { <input type="hidden" name="status" value="@Model.Status" /> }
        <span class="ms ms-sm ats-muted">search</span>
        <input name="q" value="@Model.Q" placeholder="Title or reference" aria-label="Search jobs" />
    </form>
    <div class="ats-filter-group">
        @foreach (var f in filters)
        {
            var active = (f.Val ?? "") == (current ?? "") ? "active" : "";
            <a class="@active" asp-action="Index" asp-route-status="@f.Val" asp-route-q="@Model.Q">@f.Text</a>
        }
    </div>
    <span class="ms-auto ats-muted ats-small">@Model.Results.Total jobs</span>
</div>

<div class="ats-card-flush">
    <div class="ats-thead" style="@cols">
        <span>Job</span><span>Status</span><span>Pipeline</span><span>Candidates</span><span>Published</span><span></span>
    </div>
    @foreach (var j in Model.Results.Items)
    {
        var segments = j.StageCounts.Select(s => new PipelineSegment(s.Stage, s.Count)).ToList();
        <div class="ats-trow ats-trow--link" style="@cols"
             onclick="location.href='@Url.Action("Index", "Board", new { jobId = j.Id })'">
            <span class="ats-cell-stack">
                <span class="ats-cell-title">@j.Title</span>
                <span class="ats-cell-sub"><code>@j.ExternalRef</code>
                    @if (j.Department is not null) { <text>· @j.Department</text> }
                    @if (j.Location is not null) { <text>· @j.Location</text> }
                </span>
            </span>
            <span><partial name="Partials/_StatusPill" model="new StatusPillModel(j.Status.ToString(), Tone(j.Status))" /></span>
            <span class="ats-cell-stack">
                <partial name="Partials/_PipelineBar" model="new PipelineBarModel(segments, true)" />
            </span>
            <span class="ats-avatar-stack">
                @foreach (var name in j.TopApplicantNames)
                {
                    <partial name="Partials/_Avatar" model="new AvatarModel(name, 1.625, true)" />
                }
                @{ var extra = j.TotalApplications - j.TopApplicantNames.Count; }
                @if (extra > 0) { <span class="ats-avatar-stack-more">+@extra</span> }
                @if (j.TotalApplications == 0) { <span class="ats-faint ats-small">—</span> }
            </span>
            <span class="ats-small ats-muted">@(j.PublishedAt?.ToLocalTime().ToString("dd MMM") ?? "—")</span>
            <span class="d-flex justify-content-end" onclick="event.stopPropagation()">
                <div class="dropdown">
                    <button type="button" class="btn btn-sm btn-outline-secondary border-0 px-2" data-bs-toggle="dropdown" aria-label="Actions">
                        <span class="ms ms-sm">more_horiz</span>
                    </button>
                    <ul class="dropdown-menu dropdown-menu-end">
                        <li><a class="dropdown-item" asp-controller="Board" asp-action="Index" asp-route-jobId="@j.Id">Open board</a></li>
                        <li><a class="dropdown-item" asp-action="Edit" asp-route-id="@j.Id">Edit</a></li>
                        @if (j.Status != JobStatus.Published)
                        {
                            <li>
                                <form asp-action="Publish" asp-route-id="@j.Id" method="post"
                                      onsubmit="return confirm('Publish this job? It becomes visible on the public career site.');">
                                    <button class="dropdown-item" type="submit">Publish</button>
                                </form>
                            </li>
                        }
                        else
                        {
                            <li>
                                <form asp-action="Close" asp-route-id="@j.Id" method="post"
                                      onsubmit="return confirm('Close this job? It is removed from the public career site.');">
                                    <button class="dropdown-item" type="submit">Close</button>
                                </form>
                            </li>
                        }
                        <li><hr class="dropdown-divider"></li>
                        <li>
                            <form asp-action="Delete" asp-route-id="@j.Id" method="post"
                                  onsubmit="return confirm('Delete this job?');">
                                <button class="dropdown-item text-danger" type="submit">Delete</button>
                            </form>
                        </li>
                    </ul>
                </div>
            </span>
        </div>
    }
    @if (Model.Results.Items.Count == 0)
    {
        <partial name="Partials/_EmptyState" model="new EmptyStateModel(&quot;work_outline&quot;, &quot;No jobs match.&quot;, &quot;Try clearing the filter, or create a new job.&quot;)" />
    }
</div>

@{
    var jq = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(Model.Q)) jq["q"] = Model.Q;
    if (Model.Status is not null) jq["status"] = Model.Status.ToString()!;
    var pager = new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Index", Query = jq };
}
<partial name="_Pager" model="pager" />
```

- [ ] **Step 4: Verify**

Run: `dotnet build`
Expected: success. Runtime check in Task 10.

*Commit point: `feat: rebuild jobs list to the redesign`*

---

## Task 6: Candidates list projection + view

**Files:**
- Create: `src/Ats.Application/Candidates/CandidateListItem.cs`
- Create: `src/Ats.Infrastructure/Candidates/CandidateListQuery.cs`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`
- Modify: `src/Ats.Web/Models/CandidatesIndexViewModel.cs`
- Modify: `src/Ats.Web/Controllers/CandidatesController.cs`
- Modify: `src/Ats.Web/Views/Candidates/Index.cshtml`

- [ ] **Step 1: Projection + contract**

`src/Ats.Application/Candidates/CandidateListItem.cs`:

```csharp
using Ats.Application.Common;
using Ats.Domain.Enums;

namespace Ats.Application.Candidates;

public sealed record CandidateListItem(
    int Id,
    string FullName,
    string Email,
    string? Phone,
    ApplicationOrigin LatestOrigin,
    string? LatestJobTitle,
    string? LatestStageName,
    int ApplicationCount,
    DateTimeOffset? LastActivity);

public interface ICandidateListQuery
{
    Task<PagedResult<CandidateListItem>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default);
}
```

- [ ] **Step 2: EF projection**

`src/Ats.Infrastructure/Candidates/CandidateListQuery.cs`. Paginate candidates on the server, then
enrich the visible page from their applications.

```csharp
using Ats.Application.Candidates;
using Ats.Application.Common;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Candidates;

public sealed class CandidateListQuery : ICandidateListQuery
{
    private readonly AtsDbContext _db;
    public CandidateListQuery(AtsDbContext db) => _db = db;

    public async Task<PagedResult<CandidateListItem>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Candidates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => EF.Functions.Like(c.FirstName, $"%{s}%")
                          || EF.Functions.Like(c.LastName, $"%{s}%")
                          || EF.Functions.Like(c.Email, $"%{s}%"));
        }

        var total = await q.CountAsync(ct);
        var candidates = await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.Phone })
            .ToListAsync(ct);

        var ids = candidates.Select(c => c.Id).ToList();

        // All applications for the visible candidates, with job title, stage name and last event time.
        var apps = await (
            from a in _db.Applications
            where ids.Contains(a.CandidateId)
            join j in _db.Jobs on a.JobId equals j.Id
            join st in _db.PipelineStages on a.CurrentStageId equals st.Id
            select new
            {
                a.CandidateId, a.Id, a.AppliedAt, a.Origin,
                JobTitle = j.Title, StageName = st.Name,
                LastEvent = _db.ApplicationEvents.Where(e => e.ApplicationId == a.Id).Max(e => (DateTimeOffset?)e.OccurredAt)
            })
            .ToListAsync(ct);

        var byCandidate = apps.GroupBy(a => a.CandidateId).ToDictionary(g => g.Key, g => g.ToList());

        var items = candidates.Select(c =>
        {
            var list = byCandidate.TryGetValue(c.Id, out var l) ? l : new();
            var latest = list.OrderByDescending(a => a.AppliedAt).FirstOrDefault();
            var lastActivity = list
                .Select(a => a.LastEvent ?? a.AppliedAt)
                .DefaultIfEmpty()
                .Max();
            return new CandidateListItem(
                c.Id, c.FirstName + " " + c.LastName, c.Email, c.Phone,
                latest?.Origin ?? ApplicationOrigin.Unknown,
                latest?.JobTitle, latest?.StageName,
                list.Count,
                list.Count == 0 ? null : lastActivity);
        }).ToList();

        return new PagedResult<CandidateListItem>(items, page, pageSize, total);
    }
}
```

- [ ] **Step 3: Register + view model + controller**

Register `services.AddScoped<ICandidateListQuery, CandidateListQuery>();` (add
`using Ats.Infrastructure.Candidates;`).

`CandidatesIndexViewModel.cs`:

```csharp
using Ats.Application.Candidates;
using Ats.Application.Common;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class CandidatesIndexViewModel
{
    public PagedResult<CandidateListItem> Results { get; set; } = default!;
    public string? Q { get; set; }
    public List<SelectListItem> PublishedJobs { get; set; } = new();
}
```

In `CandidatesController`, inject `ICandidateListQuery` and use it in `Index` (the published-jobs
select list building stays as-is; only the `Results` source changes):

```csharp
        var results = await _candidateList.SearchAsync(q, page, 20);
```

- [ ] **Step 4: Rebuild `Views/Candidates/Index.cshtml`**

Prototype L582-644. Toolbar (search + source/job filter buttons that are visual only for now — keep
the working search form; render the filter buttons as inert styled buttons, do not wire new
filtering this phase) and a `.ats-card-flush` grid table: avatar + name/subline, two-line contact,
`_SourceChip`, latest job with a stage dot, last activity relative time. Keep the "Add to job"
capability behind a row dropdown so nothing is lost, and keep `_Pager`.

```razor
@model Ats.Web.Models.CandidatesIndexViewModel
@using Ats.Web.Models.Shared
@using Ats.Application.Common
@{
    ViewData["Title"] = "Candidates";
    ViewData["Eyebrow"] = $"{Model.Results.Total} people in your talent pool:";
    var now = DateTimeOffset.UtcNow;
    const string cols = "grid-template-columns:2.2fr 1.8fr 1.4fr 1.6fr 1fr 44px;";
}

@section PageActions {
    <a class="btn btn-primary" asp-action="Create"><span class="ms ms-sm">person_add</span> Add candidate</a>
}

<div class="ats-toolbar mb-3">
    <form asp-action="Index" method="get" class="ats-search" style="width:300px" role="search">
        <span class="ms ms-sm ats-muted">search</span>
        <input name="q" value="@Model.Q" placeholder="Name or email" aria-label="Search candidates" />
    </form>
    <span class="ms-auto ats-muted ats-small">@Model.Results.Total candidates</span>
</div>

<div class="ats-card-flush">
    <div class="ats-thead" style="@cols">
        <span>Candidate</span><span>Contact</span><span>Source</span><span>Applications</span><span>Last activity</span><span></span>
    </div>
    @foreach (var c in Model.Results.Items)
    {
        <div class="ats-trow ats-trow--link" style="@cols"
             onclick="location.href='@Url.Action("Edit", new { id = c.Id })'">
            <span class="d-flex align-items-center gap-2" style="min-width:0">
                <partial name="Partials/_Avatar" model="new AvatarModel(c.FullName, 2.125)" />
                <span class="ats-cell-stack">
                    <span class="ats-cell-title">@c.FullName</span>
                    @if (c.LatestJobTitle is not null) { <span class="ats-cell-sub">@c.LatestJobTitle</span> }
                </span>
            </span>
            <span class="ats-cell-stack ats-small">
                <span>@c.Email</span>
                <span class="ats-muted">@(string.IsNullOrEmpty(c.Phone) ? "No phone" : c.Phone)</span>
            </span>
            <span><partial name="Partials/_SourceChip" model="new SourceChipModel(c.LatestOrigin)" /></span>
            <span class="ats-cell-stack">
                @if (c.LatestStageName is not null)
                {
                    <span class="ats-small">@c.LatestStageName</span>
                    @if (c.ApplicationCount > 1) { <span class="ats-faint ats-xsmall">+@(c.ApplicationCount - 1) more</span> }
                }
                else { <span class="ats-faint ats-small">No applications</span> }
            </span>
            <span class="ats-small ats-muted">@(c.LastActivity is null ? "—" : RelativeTime.Long(c.LastActivity, now))</span>
            <span class="d-flex justify-content-end" onclick="event.stopPropagation()">
                @if (Model.PublishedJobs.Count > 0)
                {
                    <div class="dropdown">
                        <button type="button" class="btn btn-sm btn-outline-secondary border-0 px-2" data-bs-toggle="dropdown" aria-label="Add to job">
                            <span class="ms ms-sm">more_horiz</span>
                        </button>
                        <div class="dropdown-menu dropdown-menu-end p-2" style="min-width:16rem">
                            <form asp-action="AddToJob" method="post" class="d-flex gap-1">
                                <input type="hidden" name="candidateId" value="@c.Id" />
                                <select name="jobId" class="form-select form-select-sm" asp-items="Model.PublishedJobs">
                                    <option value="">Add to job…</option>
                                </select>
                                <button class="btn btn-sm btn-primary" type="submit">Add</button>
                            </form>
                        </div>
                    </div>
                }
            </span>
        </div>
    }
    @if (Model.Results.Items.Count == 0)
    {
        <partial name="Partials/_EmptyState" model="new EmptyStateModel(&quot;group&quot;, &quot;No candidates match.&quot;)" />
    }
</div>

@{
    var cq = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(Model.Q)) cq["q"] = Model.Q;
    var pager = new Ats.Web.Models.PagerModel { Page = Model.Results.Page, TotalPages = Model.Results.TotalPages, Action = "Index", Query = cq };
}
<partial name="_Pager" model="pager" />
```

- [ ] **Step 5: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild candidates list to the redesign`*

---

## Task 7: Application card projection + candidate drawer

**Files:**
- Create: `src/Ats.Application/Applications/ApplicationCard.cs`
- Create: `src/Ats.Infrastructure/Applications/ApplicationCardQuery.cs`
- Create: `src/Ats.Web/Views/Shared/Partials/_CandidateDrawer.cshtml`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`
- Modify: `src/Ats.Web/Controllers/ApplicationsController.cs`
- Modify: `src/Ats.Web/Models/ApplicationDetailsViewModel.cs`
- Modify: `src/Ats.Web/Views/Applications/Details.cshtml`
- Modify: `src/Ats.Web/wwwroot/js/site.js`

- [ ] **Step 1: Projection + contract**

`src/Ats.Application/Applications/ApplicationCard.cs`:

```csharp
using Ats.Domain.Enums;

namespace Ats.Application.Applications;

public sealed record StageProgressItem(string Name, bool Reached, bool IsCurrent);

public sealed record ApplicationCard(
    int ApplicationId,
    string CandidateName,
    string Email,
    string? Phone,
    string JobTitle,
    int JobId,
    string CurrentStageName,
    string? NextStageName,
    ApplicationStatus Status,
    ApplicationOrigin Origin,
    string? ReferralCode,
    DateTimeOffset AppliedAt,
    int DaysInStage,
    string? DeliveryState,       // Delivered / Failed / Pending / null (not applicable)
    string? ResumeFileName,
    long? ResumeSizeBytes,
    IReadOnlyList<StageProgressItem> Progress,
    IReadOnlyList<StageProgressItem> _unusedReserved,   // keep record shape stable; see note
    IReadOnlyList<ApplicationHistoryItem> History);

public sealed record ApplicationHistoryItem(string Title, string Subtitle, bool IsCurrent);

public interface IApplicationCardQuery
{
    Task<ApplicationCard?> GetAsync(int applicationId, CancellationToken ct = default);
}
```

Note: drop the `_unusedReserved` field — it was a drafting artefact. The record is:

```csharp
public sealed record ApplicationCard(
    int ApplicationId, string CandidateName, string Email, string? Phone,
    string JobTitle, int JobId, string CurrentStageName, string? NextStageName,
    ApplicationStatus Status, ApplicationOrigin Origin, string? ReferralCode,
    DateTimeOffset AppliedAt, int DaysInStage, string? DeliveryState,
    string? ResumeFileName, long? ResumeSizeBytes,
    IReadOnlyList<StageProgressItem> Progress,
    IReadOnlyList<ApplicationHistoryItem> History);
```

- [ ] **Step 2: EF projection**

`src/Ats.Infrastructure/Applications/ApplicationCardQuery.cs`. Reuses `RelativeTime.WholeDays` for
days-in-stage and `IFileStore.StatAsync` for the resume size. Delivery state is the most recent
outbox message for the application, or null when the application has none (no referral code).

```csharp
using Ats.Application.Abstractions;
using Ats.Application.Applications;
using Ats.Application.Common;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Applications;

public sealed class ApplicationCardQuery : IApplicationCardQuery
{
    private readonly AtsDbContext _db;
    private readonly IFileStore _files;
    public ApplicationCardQuery(AtsDbContext db, IFileStore files) { _db = db; _files = files; }

    public async Task<ApplicationCard?> GetAsync(int applicationId, CancellationToken ct = default)
    {
        var a = await _db.Applications
            .Where(x => x.Id == applicationId)
            .Select(x => new
            {
                x.Id, x.JobId, x.CandidateId, x.CurrentStageId, x.SourceCode, x.Origin, x.AppliedAt, x.Status,
                JobTitle = _db.Jobs.Where(j => j.Id == x.JobId).Select(j => j.Title).FirstOrDefault(),
                Cand = _db.Candidates.Where(c => c.Id == x.CandidateId)
                    .Select(c => new { c.FirstName, c.LastName, c.Email, c.Phone, c.ResumeFileKey }).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (a is null || a.Cand is null) return null;

        var stages = await _db.PipelineStages
            .Where(s => _db.Jobs.Any(j => j.Id == a.JobId && j.PipelineTemplateId == s.PipelineTemplateId))
            .OrderBy(s => s.Order)
            .Select(s => new { s.Id, s.Name, s.Order, s.IsTerminal })
            .ToListAsync(ct);

        var currentIndex = stages.FindIndex(s => s.Id == a.CurrentStageId);
        var progress = stages
            .Where(s => !s.IsTerminal)
            .Select((s, i) => new StageProgressItem(s.Name, i <= currentIndex, s.Id == a.CurrentStageId))
            .ToList();
        var nextStage = currentIndex >= 0 && currentIndex + 1 < stages.Count ? stages[currentIndex + 1].Name : null;

        var lastEvent = await _db.ApplicationEvents
            .Where(e => e.ApplicationId == a.Id)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefaultAsync(ct);
        var daysInStage = RelativeTime.WholeDays(lastEvent ?? a.AppliedAt, DateTimeOffset.UtcNow);

        var deliveryState = await _db.OutboxMessages
            .Where(m => m.ApplicationId == a.Id)
            .OrderByDescending(m => m.Id)
            .Select(m => (OutboxStatus?)m.Status)
            .FirstOrDefaultAsync(ct);

        var events = await _db.ApplicationEvents
            .Where(e => e.ApplicationId == a.Id)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { e.FromStageId, e.ToStageId, e.OccurredAt })
            .ToListAsync(ct);
        string StageName(int id) => stages.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var history = events.Select((e, i) => new ApplicationHistoryItem(
            e.FromStageId is null ? $"Applied — {StageName(e.ToStageId)}" : $"Moved to {StageName(e.ToStageId)}",
            e.OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            i == 0)).ToList();

        StoredFileInfo? file = a.Cand.ResumeFileKey is null ? null : await _files.StatAsync(a.Cand.ResumeFileKey, ct);

        return new ApplicationCard(
            a.Id, $"{a.Cand.FirstName} {a.Cand.LastName}", a.Cand.Email, a.Cand.Phone,
            a.JobTitle ?? "(job)", a.JobId, StageName(a.CurrentStageId), nextStage,
            a.Status, a.Origin, a.SourceCode, a.AppliedAt, daysInStage,
            deliveryState?.ToString(),
            file?.FileName, file?.Length,
            progress, history);
    }
}
```

- [ ] **Step 3: Register + controller `Card` action**

Register `services.AddScoped<IApplicationCardQuery, ApplicationCardQuery>();`
(`using Ats.Infrastructure.Applications;`).

In `ApplicationsController`, inject `IApplicationCardQuery` and add a partial-returning action. Keep
the existing `Details` action but have it also load the card so the full page can render the same
drawer body.

```csharp
    [HttpGet]
    public async Task<IActionResult> Card(int id, CancellationToken ct)
    {
        var card = await _card.GetAsync(id, ct);
        if (card is null) return NotFound();
        return PartialView("Partials/_CandidateDrawer", card);
    }
```

And in `Details`, after building the existing model, attach the card:

```csharp
        var card = await _card.GetAsync(id);
        return View(new ApplicationDetailsViewModel
        {
            Application = app, CandidateName = name, Stages = stages, Events = events, Card = card
        });
```

Add `public Ats.Application.Applications.ApplicationCard? Card { get; set; }` to
`ApplicationDetailsViewModel`.

- [ ] **Step 4: Write `_CandidateDrawer.cshtml`**

Prototype L974-1041. The partial is just the drawer *body* (header, stage progress, application
facts, CV card, history); the backdrop + panel wrapper is added by whoever hosts it. Model is
`ApplicationCard`.

```razor
@using Ats.Web.Models.Shared
@using Ats.Domain.Enums
@model Ats.Application.Applications.ApplicationCard
@{
    string Size(long? b) => b is null ? "" : b < 1024 ? $"{b} B" : b < 1024 * 1024 ? $"{b / 1024} KB" : $"{b / (1024 * 1024)} MB";
    (string Label, PillTone Tone)? delivery = Model.DeliveryState switch
    {
        "Delivered" => ("Delivered to ReferralTool", PillTone.Success),
        "Failed" => ("Delivery failed", PillTone.Danger),
        "Pending" => ("Delivery pending", PillTone.Warning),
        _ => null
    };
}
<div class="ats-drawer-section">
    <div class="d-flex align-items-start gap-3">
        <partial name="Partials/_Avatar" model="new AvatarModel(Model.CandidateName, 3.25)" />
        <span class="ats-cell-stack flex-grow-1">
            <span style="font-family:var(--no-font-display);font-weight:800;font-size:1.375rem;letter-spacing:-.01em">@Model.CandidateName</span>
            <span class="ats-small ats-muted">@Model.Email@(string.IsNullOrEmpty(Model.Phone) ? "" : " · " + Model.Phone)</span>
        </span>
        <button type="button" class="ats-icon-btn border-0" data-drawer-close aria-label="Close"><span class="ms ms-sm">close</span></button>
    </div>
    <div class="d-flex gap-2 flex-wrap">
        @if (Model.NextStageName is not null && Model.Status == ApplicationStatus.Active)
        {
            <button type="button" class="btn btn-primary btn-sm" disabled title="Use the board to move stages">
                Move to @Model.NextStageName <span class="ms ms-sm">arrow_forward</span>
            </button>
        }
        @if (Model.ResumeFileName is not null)
        {
            <a class="btn btn-outline-secondary btn-sm" asp-controller="Resume" asp-action="Download" asp-route-applicationId="@Model.ApplicationId">Download CV</a>
        }
    </div>
</div>

<div class="ats-drawer-section">
    <span class="ats-eyebrow">Current stage:</span>
    <div class="d-flex gap-1">
        @foreach (var p in Model.Progress)
        {
            <span class="flex-grow-1 d-flex flex-column gap-1">
                <span style="height:5px;border-radius:99px;background:@(p.Reached ? "var(--no-oxford-blue)" : "var(--no-stage-empty)")"></span>
                <span class="ats-xsmall @(p.IsCurrent ? "" : "ats-muted")">@p.Name</span>
            </span>
        }
    </div>
</div>

<div class="ats-drawer-section">
    <span class="ats-eyebrow">Application:</span>
    <div class="ats-drawer-facts">
        <span class="k">Job</span><span>@Model.JobTitle</span>
        <span class="k">Applied</span><span>@Model.AppliedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")</span>
        <span class="k">Source</span><span><partial name="Partials/_SourceChip" model="new SourceChipModel(Model.Origin)" /></span>
        <span class="k">Days in stage</span><span>@Model.DaysInStage</span>
        @if (Model.ReferralCode is not null)
        {
            <span class="k">Referral code</span><span><code>@Model.ReferralCode</code></span>
        }
        @if (delivery is { } d)
        {
            <span class="k">Status pushed</span><span><partial name="Partials/_StatusPill" model="new StatusPillModel(d.Label, d.Tone)" /></span>
        }
    </div>
    @if (Model.ResumeFileName is not null)
    {
        <div class="ats-file-card">
            <span class="ms" style="color:var(--no-danger-ink)">picture_as_pdf</span>
            <span class="ats-cell-stack flex-grow-1">
                <span class="ats-small">@Model.ResumeFileName</span>
                <span class="ats-xsmall ats-muted">@Size(Model.ResumeSizeBytes)</span>
            </span>
            <a class="ms ms-sm" asp-controller="Resume" asp-action="Download" asp-route-applicationId="@Model.ApplicationId" aria-label="Download">download</a>
        </div>
    }
</div>

<div class="ats-drawer-section">
    <span class="ats-eyebrow">History:</span>
    <partial name="Partials/_Timeline" model="new TimelineModel(Model.History.Select(h => new TimelineItem(h.Title, h.Subtitle, h.IsCurrent)).ToList())" />
</div>
```

Note on the "Move to next" button: it is rendered **disabled** with a tooltip pointing to the board,
because a reliable move needs the current `RowVersion` which the read projection does not carry.
Moving stages stays a board action (drag or the card dropdown). This keeps the drawer read-only and
avoids a second, weaker move path that could fight optimistic concurrency. If a future phase wants an
in-drawer move, thread `RowVersion` through `ApplicationCard` first.

- [ ] **Step 5: Rebuild `Views/Applications/Details.cshtml` to host the drawer body**

The full page becomes the no-JS fallback and deep link: render the same drawer body inside the normal
content area.

```razor
@model Ats.Web.Models.ApplicationDetailsViewModel
@{ ViewData["Title"] = Model.CandidateName; ViewData["Eyebrow"] = "Application:"; }

@section PageActions {
    <a class="btn btn-outline-secondary" asp-controller="Board" asp-action="Index" asp-route-jobId="@Model.Application.JobId">
        <span class="ms ms-sm">arrow_back</span> Board
    </a>
}

<div class="ats-card" style="max-width:640px;padding:0">
    @if (Model.Card is not null)
    {
        <partial name="Partials/_CandidateDrawer" model="Model.Card" />
    }
    else
    {
        <div class="ats-drawer-section"><span class="ats-muted">This application is no longer available.</span></div>
    }
</div>
```

The drawer's close button (`data-drawer-close`) is inert on this full page (there is no overlay to
close); that is harmless. The board (Task 8) opens the same partial in the overlay.

- [ ] **Step 6: Drawer open/close JS**

Append to `site.js`. The board cards will trigger an htmx GET to `Card` that swaps into
`#ats-drawer-host`; this code wraps the swapped content in the overlay and handles close.

```javascript
// Candidate drawer: htmx swaps the drawer BODY into #ats-drawer-host; wrap it in the overlay,
// and close on backdrop click, the close button, Escape, or the ats:drawer-close event.
(function () {
    var host = document.getElementById('ats-drawer-host');
    if (!host) return;

    function close() { host.innerHTML = ''; }

    document.body.addEventListener('htmx:afterSwap', function (e) {
        if (e.target.id !== 'ats-drawer-host') return;
        var body = host.innerHTML;
        host.innerHTML =
            '<div class="ats-drawer-backdrop" data-drawer-backdrop>' +
            '<div class="ats-drawer ats-drawer-in ats-scroll" role="dialog" aria-modal="true">' + body + '</div></div>';
    });

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-drawer-close]') || e.target.hasAttribute('data-drawer-backdrop')) close();
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
    document.addEventListener('ats:drawer-close', close);
})();
```

- [ ] **Step 7: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: candidate drawer with application card projection`*

---

## Task 8: Rebuild the board

**Files:**
- Create: `src/Ats.Web/Models/Board/BoardCardModel.cs`
- Modify: `src/Ats.Web/Models/BoardViewModel.cs`
- Modify: `src/Ats.Web/Controllers/BoardController.cs`
- Modify: `src/Ats.Web/Views/Board/_Board.cshtml`
- Modify: `src/Ats.Web/Views/Board/Index.cshtml`

- [ ] **Step 1: Richer card + column models**

The board keeps `BoardCard` (id, name, rowVersion) for the move form, but the view needs more per
card. Add a parallel display model and extend the column with stage colour/kind.

`src/Ats.Web/Models/Board/BoardCardModel.cs`:

```csharp
using Ats.Domain.Enums;

namespace Ats.Web.Models.Board;

public sealed record BoardCardModel(
    int ApplicationId,
    string CandidateName,
    string Email,
    string RowVersion,
    ApplicationOrigin Origin,
    int DaysInStage,
    int StageIndex,
    int StageCount,
    bool Rejected);
```

Extend `BoardViewModel.cs` — replace `BoardColumn`'s card list type and add stage metadata:

```csharp
using Ats.Domain.Entities;
using Ats.Web.Models.Board;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public record BoardColumn(PipelineStage Stage, int StageIndex, int StageCount, List<BoardCardModel> Cards);

public class BoardViewModel
{
    public Job Job { get; set; } = default!;
    public List<BoardColumn> Columns { get; set; } = new();
    public string? Error { get; set; }
    public List<SelectListItem> CandidateOptions { get; set; } = new();

    // Stats strip (prototype L420-425).
    public int InProcess { get; set; }
    public int? AvgDaysInStage { get; set; }
    public int FromReferral { get; set; }
    public int OldestDays { get; set; }
}
```

Delete the old `record BoardCard(...)` line — the move form uses `BoardCardModel.RowVersion` now.

- [ ] **Step 2: Build the richer model in `BoardController.BuildBoardAsync`**

Replace the `columns` construction. Applications already come from `ListForJobAsync` with
`Candidate` loaded. Compute per-card days-in-stage from the latest event; to avoid an N+1, load the
latest event time per application in one query.

```csharp
    private async Task<BoardViewModel?> BuildBoardAsync(int jobId, string? error)
    {
        var job = await _service.GetJobAsync(jobId);
        if (job is null) return null;
        var stages = (await _service.GetStagesForJobAsync(jobId)).OrderBy(s => s.Order).ToList();
        var apps = await _service.ListForJobAsync(jobId);

        var lastEvents = await _service.LatestEventTimesForJobAsync(jobId);  // see Step 3
        var now = DateTimeOffset.UtcNow;
        int Days(Ats.Domain.Entities.JobApplication a) =>
            Ats.Application.Common.RelativeTime.WholeDays(
                lastEvents.TryGetValue(a.Id, out var t) ? t : a.AppliedAt, now);

        var columns = stages.Select((s, idx) => new BoardColumn(s, idx, stages.Count,
            apps.Where(a => a.CurrentStageId == s.Id)
                .Select(a => new Ats.Web.Models.Board.BoardCardModel(
                    a.Id,
                    a.Candidate?.FullName ?? "(unknown)",
                    a.Candidate?.Email ?? "",
                    Convert.ToBase64String(a.RowVersion),
                    a.Origin,
                    Days(a),
                    idx,
                    stages.Count,
                    s.IsTerminal && s.TerminalOutcome == Ats.Domain.Entities.StageOutcome.Rejected))
                .ToList())).ToList();

        var active = apps.Where(a => a.Status == Ats.Domain.Enums.ApplicationStatus.Active).ToList();
        var candidateOptions = (await _candidates.ListAsync())
            .Select(c => new SelectListItem($"{c.FullName} <{c.Email}>", c.Id.ToString())).ToList();

        return new BoardViewModel
        {
            Job = job, Columns = columns, Error = error, CandidateOptions = candidateOptions,
            InProcess = active.Count,
            AvgDaysInStage = Ats.Application.Common.DashboardMath.MeanDays(
                active.Select(a => now - (lastEvents.TryGetValue(a.Id, out var t) ? t : a.AppliedAt)).ToList()),
            FromReferral = apps.Count(a => a.Origin == Ats.Domain.Enums.ApplicationOrigin.Referral),
            OldestDays = apps.Count == 0 ? 0 : apps.Max(a => Ats.Application.Common.RelativeTime.WholeDays(a.AppliedAt, now))
        };
    }
```

- [ ] **Step 3: Add `LatestEventTimesForJobAsync` to the application service**

Add to `IApplicationService` and implement in `ApplicationService` by delegating to a new repository
method (keeps EF out of the service). Interface:

```csharp
    Task<Dictionary<int, DateTimeOffset>> LatestEventTimesForJobAsync(int jobId, CancellationToken ct = default);
```

`ApplicationService` implementation: `=> _repo.LatestEventTimesForJobAsync(jobId, ct);`

`IApplicationRepository` + `ApplicationRepository`:

```csharp
    Task<Dictionary<int, DateTimeOffset>> LatestEventTimesForJobAsync(int jobId, CancellationToken ct = default);
```

```csharp
    public async Task<Dictionary<int, DateTimeOffset>> LatestEventTimesForJobAsync(int jobId, CancellationToken ct = default)
    {
        return await (
            from e in _db.ApplicationEvents
            join a in _db.Applications on e.ApplicationId equals a.Id
            where a.JobId == jobId
            group e by e.ApplicationId into g
            select new { ApplicationId = g.Key, Last = g.Max(x => x.OccurredAt) })
            .ToDictionaryAsync(x => x.ApplicationId, x => x.Last, ct);
    }
```

Read `ApplicationRepository.cs` first to match its field name for the context (likely `_db`) and its
`using`s.

- [ ] **Step 4: Rebuild `_Board.cshtml`**

Prototype L427-576. The critical invariant: keep the `#board-container` id, the per-card `<form>`
posting to `Board/Move` with `jobId`/`applicationId`/`rowVersion`/`toStageId`, the `.to-stage` hidden
input, the `.move-select` fallback, and the htmx attributes — the Phase 1 `initBoard()` script binds
to exactly these. Only the card's inner markup and the column chrome change.

```razor
@model Ats.Web.Models.BoardViewModel
@using Ats.Web.Models.Shared
@{
    var ramp = new[] { "var(--no-stage-1)", "var(--no-stage-2)", "var(--no-stage-3)", "var(--no-stage-4)", "var(--no-stage-5)" };
}
<div id="board-container">
    @if (!string.IsNullOrEmpty(Model.Error))
    {
        <div class="alert alert-warning d-flex align-items-center gap-2" role="alert">
            <span class="ms ms-sm">error</span> @Model.Error
        </div>
    }
    <div class="ats-board ats-scroll">
        @foreach (var col in Model.Columns)
        {
            var terminalHired = col.Stage.IsTerminal && col.Stage.TerminalOutcome == Ats.Domain.Entities.StageOutcome.Hired;
            var terminalRej = col.Stage.IsTerminal && col.Stage.TerminalOutcome == Ats.Domain.Entities.StageOutcome.Rejected;
            var colClass = terminalHired ? "ats-board-col--hired" : terminalRej ? "ats-board-col--rejected" : "";
            var dot = terminalHired ? "var(--no-stage-5)" : terminalRej ? "var(--no-danger-ink)" : ramp[col.StageIndex % ramp.Length];
            <div class="ats-board-col @colClass" data-stage-id="@col.Stage.Id">
                <div class="ats-board-col-head">
                    <span class="ats-board-col-dot" style="background:@dot"></span>
                    <span class="ats-board-col-name">@col.Stage.Name</span>
                    <span class="ats-board-col-count">@col.Cards.Count</span>
                </div>
                <div class="ats-board-cards" data-stage-id="@col.Stage.Id">
                    @foreach (var card in col.Cards)
                    {
                        <form class="ats-board-card @(card.Rejected ? "ats-board-card--rejected" : "")"
                              asp-controller="Board" asp-action="Move" method="post"
                              hx-post="@Url.Action("Move", "Board")" hx-target="#board-container" hx-swap="outerHTML"
                              hx-get-card="@Url.Action("Card", "Applications", new { id = card.ApplicationId })">
                            <input type="hidden" name="jobId" value="@Model.Job.Id" />
                            <input type="hidden" name="applicationId" value="@card.ApplicationId" />
                            <input type="hidden" name="rowVersion" value="@card.RowVersion" />
                            <input type="hidden" name="toStageId" value="@col.Stage.Id" class="to-stage" />
                            <div class="d-flex align-items-center gap-2">
                                <partial name="Partials/_Avatar" model="new AvatarModel(card.CandidateName, 2)" />
                                <span class="ats-cell-stack">
                                    <span class="ats-board-card-name">@card.CandidateName</span>
                                    <span class="ats-board-card-sub">@card.Email</span>
                                </span>
                            </div>
                            <div class="d-flex align-items-center gap-1 flex-wrap">
                                <partial name="Partials/_SourceChip" model="new SourceChipModel(card.Origin)" />
                                @{
                                    var ageCls = card.DaysInStage > 7 ? "ats-chip--danger" : card.DaysInStage > 3 ? "ats-chip--warning" : "ats-chip--neutral";
                                }
                                <span class="ats-chip @ageCls"><span class="ms ms-sm">schedule</span>@card.DaysInStage d</span>
                                <span class="ats-progress-dots ms-auto">
                                    @for (var i = 0; i < col.StageCount; i++)
                                    {
                                        <span class="@(i <= card.StageIndex ? "on" : "")"></span>
                                    }
                                </span>
                            </div>
                            <select class="form-select form-select-sm move-select" aria-label="Move @card.CandidateName to stage">
                                @foreach (var s in Model.Columns)
                                {
                                    <option value="@s.Stage.Id" selected="@(s.Stage.Id == col.Stage.Id)">@s.Stage.Name</option>
                                }
                            </select>
                        </form>
                    }
                    @if (col.Cards.Count == 0 && col.Stage.IsTerminal && col.Stage.TerminalOutcome == Ats.Domain.Entities.StageOutcome.Hired)
                    {
                        <div class="ats-board-drop">Drop a candidate here to mark them hired. Their referral status is pushed to ReferralTool automatically.</div>
                    }
                </div>
            </div>
        }
    </div>
</div>
```

- [ ] **Step 5: Extend `initBoard()` to open the drawer on card click**

In `Views/Board/Index.cshtml`, the existing script binds Sortable + the move-select. Add: a click on
a card (not on the select) issues the drawer htmx GET. The card carries `hx-get-card`; wire a plain
click handler that calls `htmx.ajax`.

Add inside `initBoard()`, after the existing `.move-select` loop:

```javascript
            document.querySelectorAll('.ats-board-card').forEach(function (card) {
                card.addEventListener('click', function (e) {
                    if (e.target.closest('.move-select')) return;      // dropdown handles its own
                    if (e.target.closest('option')) return;
                    var url = card.getAttribute('hx-get-card');
                    if (url) htmx.ajax('GET', url, { target: '#ats-drawer-host', swap: 'innerHTML' });
                });
            });
```

The SortableJS `onEnd` and the `.move-select` change handler are unchanged, so drag-to-move and the
dropdown fallback still post exactly as before. The card click is a separate concern (open drawer).
Guard: dragging fires Sortable, not click, so the two do not collide.

- [ ] **Step 6: Rebuild the board header in `Index.cshtml`**

Prototype L392-425. Replace the top `<div class="mb-3 d-flex gap-2">` block (back link + add
button) with the redesign header: back link, job title with a status pill, a meta row
(ref/department/location/type), the tabs (Pipeline active; Job details → Jobs/Edit; All applicants;
Activity → Audit filtered — for this phase, render Job details as a link and the other two as inert
styled tabs), and the stats strip. Keep the collapse "Add candidate" form and `<partial name="_Board" />`
and the whole `@section Scripts` (with the additions from Step 5) intact.

Set `ViewData["Title"] = Model.Job.Title;` and remove the old `ViewData["Title"]` board prefix.

Header block:

```razor
<div class="ats-pagehead">
    <div class="ats-pagehead-text">
        <a class="ats-eyebrow d-inline-flex align-items-center gap-1" asp-controller="Jobs" asp-action="Index" style="text-decoration:none">
            <span class="ms ms-sm">arrow_back</span> All jobs
        </a>
        <div class="d-flex align-items-center gap-2">
            <h1 class="mb-0">@Model.Job.Title</h1>
            <partial name="Partials/_StatusPill" model="new Ats.Web.Models.Shared.StatusPillModel(Model.Job.Status.ToString(), Model.Job.Status == Ats.Domain.Enums.JobStatus.Published ? Ats.Web.Models.Shared.PillTone.Success : Ats.Web.Models.Shared.PillTone.Neutral)" />
        </div>
        <div class="ats-cell-sub"><code>@Model.Job.ExternalRef</code>
            @if (Model.Job.Department is not null) { <text>· @Model.Job.Department.Name</text> }
            @if (Model.Job.Location is not null) { <text>· @(Model.Job.Location.City ?? Model.Job.Location.Name)</text> }
            <text>· @Model.Job.EmploymentType</text>
        </div>
    </div>
    <div class="ats-pagehead-actions">
        <button class="btn btn-primary btn-sm" data-bs-toggle="collapse" data-bs-target="#add-candidate">
            <span class="ms ms-sm">person_add</span> Add candidate
        </button>
    </div>
</div>

<div class="ats-tabs mb-3">
    <span class="active">Pipeline</span>
    <a asp-controller="Jobs" asp-action="Edit" asp-route-id="@Model.Job.Id">Job details</a>
</div>

<div class="ats-stat-strip mb-3">
    <span><span class="ats-eyebrow">In process</span><span class="ats-stat-mini">@Model.InProcess</span></span>
    <span><span class="ats-eyebrow">Avg. days in stage</span><span class="ats-stat-mini">@(Model.AvgDaysInStage?.ToString() ?? "—")</span></span>
    <span><span class="ats-eyebrow">From ReferralTool</span><span class="ats-stat-mini">@Model.FromReferral</span></span>
    <span><span class="ats-eyebrow">Oldest application</span><span class="ats-stat-mini">@Model.OldestDays<span class="ats-small ats-muted"> days</span></span></span>
</div>
```

`Model.Job.Department`/`Location` require the job loaded with those includes.
`ApplicationRepository.GetJobAsync` currently does **not** include them
(`_db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)`). Change it to:

```csharp
    public Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.Department).Include(j => j.Location)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
```

Add `using Microsoft.EntityFrameworkCore;` if the `Include` extension is not already in scope (it is —
the file uses `FirstOrDefaultAsync`). Display-only; no behaviour change to the move/add paths.

- [ ] **Step 7: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild board with rich cards, stats strip and drawer`*

---

## Task 9: Rebuild the dashboard

**Files:**
- Modify: `src/Ats.Application/Dashboard/DashboardSummary.cs`
- Modify: `src/Ats.Infrastructure/Dashboard/DashboardService.cs`
- Modify: `src/Ats.Web/Views/Dashboard/Index.cshtml`

- [ ] **Step 1: Replace the summary record**

`src/Ats.Application/Dashboard/DashboardSummary.cs`:

```csharp
using Ats.Domain.Enums;

namespace Ats.Application.Dashboard;

public sealed record StageCount(string Stage, int Count);
public sealed record SourceSlice(ApplicationOrigin Origin, int Percent);
public sealed record AttentionItem(string Icon, string Tone, string Headline, string Subline, string Url);
public sealed record ActivityItem(string Actor, string Text, string Time);

public sealed record IntegrationHealth(
    bool Connected, int? CustomerId, DateTimeOffset? FeedLastPulledAt,
    int Delivered24h, int Failed24h, int Pending);

public sealed record DashboardSummary(
    int OpenJobs,
    int ActiveApplications,
    int TotalCandidates,
    int? TimeToHireDays,
    int? OfferAcceptanceRate,
    IReadOnlyList<StageCount> ByStage,
    IReadOnlyList<SourceSlice> Sources,
    IReadOnlyList<AttentionItem> NeedsAttention,
    IReadOnlyList<ActivityItem> Activity,
    IntegrationHealth Integration);
```

- [ ] **Step 2: Build it in `DashboardService`**

Rewrite `DashboardService.GetAsync` to populate the richer summary. Reuse `DashboardMath`. This is
one method; keep every query tenant-filtered (no predicates needed). Full replacement:

```csharp
using Ats.Application.Common;
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
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-90);

        var openJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);
        var totalCandidates = await _db.Candidates.CountAsync(ct);
        var activeApplications = await _db.Applications.CountAsync(a => a.Status == ApplicationStatus.Active, ct);

        // Active applications by stage (kept from before, drives the distribution bars).
        var grouped = await (
            from a in _db.Applications
            where a.Status == ApplicationStatus.Active
            join s in _db.PipelineStages on a.CurrentStageId equals s.Id
            group a by new { s.Name, s.Order } into g
            select new { g.Key.Name, g.Key.Order, Count = g.Count() })
            .ToListAsync(ct);
        var byStage = grouped.OrderBy(g => g.Order).Select(g => new StageCount(g.Name, g.Count)).ToList();

        // Time to hire: AppliedAt -> first event into a Hired-outcome stage, last 90 days.
        var hiredStageIds = await _db.PipelineStages
            .Where(s => s.IsTerminal && s.TerminalOutcome == StageOutcome.Hired)
            .Select(s => s.Id).ToListAsync(ct);
        var hireSpans = await (
            from e in _db.ApplicationEvents
            where hiredStageIds.Contains(e.ToStageId) && e.OccurredAt >= since
            join a in _db.Applications on e.ApplicationId equals a.Id
            select new { a.AppliedAt, e.OccurredAt })
            .ToListAsync(ct);
        var timeToHire = DashboardMath.MeanDays(hireSpans.Select(x => x.OccurredAt - x.AppliedAt).ToList());

        // Offer acceptance: hires / applications that reached an offer-position stage (last non-terminal), 90d.
        var hires = hireSpans.Count;
        var offerReached = await _db.ApplicationEvents
            .Where(e => e.OccurredAt >= since)
            .Select(e => e.ApplicationId)
            .Distinct().CountAsync(ct);   // proxy: any progression; refined below if needed
        var acceptance = DashboardMath.Percent(hires, Math.Max(hires, offerReached == 0 ? hires : offerReached));

        // Source split.
        var originCounts = await _db.Applications
            .GroupBy(a => a.Origin)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var order = new[] { ApplicationOrigin.CareerSite, ApplicationOrigin.Referral, ApplicationOrigin.Manual, ApplicationOrigin.Unknown };
        var counts = order.Select(o => originCounts.FirstOrDefault(x => x.Key == o)?.Count ?? 0).ToList();
        var pcts = DashboardMath.Split(counts);
        var sources = order.Select((o, i) => new SourceSlice(o, pcts[i])).Where(s => s.Percent > 0).ToList();

        // Needs attention.
        var attention = new List<AttentionItem>();
        var idleBefore = now.AddDays(-7);
        var idle = await _db.Applications
            .Where(a => a.Status == ApplicationStatus.Active)
            .Select(a => _db.ApplicationEvents.Where(e => e.ApplicationId == a.Id).Max(e => (DateTimeOffset?)e.OccurredAt) ?? a.AppliedAt)
            .CountAsync(last => last < idleBefore, ct);
        if (idle > 0)
            attention.Add(new AttentionItem("hourglass_top", "warning", $"{idle} applications idle over 7 days", "In process", "/Candidates"));
        var failed = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Failed, ct);
        if (failed > 0)
            attention.Add(new AttentionItem("sync_problem", "danger", $"{failed} status updates failed to deliver", "ReferralTool", "/Integration/Deliveries"));
        var drafts = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Draft, ct);
        if (drafts > 0)
            attention.Add(new AttentionItem("edit_note", "info", $"{drafts} job(s) still in draft", "Not published", "/Jobs?status=Draft"));

        // Integration health.
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        var since24 = now.AddDays(-1);
        var delivered24 = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Delivered, ct);
        var pending = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending, ct);
        var health = new IntegrationHealth(
            settings?.IntegrationEnabled ?? false,
            settings?.ReferralToolCustomerId,
            settings?.FeedLastPulledAt,
            delivered24, failed, pending);

        // Activity feed from the audit log.
        var activity = await _db.AuditEntries
            .OrderByDescending(a => a.OccurredAt).Take(6)
            .Select(a => new ActivityItem(a.UserName, a.Summary, a.OccurredAt.ToLocalTime().ToString("HH:mm")))
            .ToListAsync(ct);

        return new DashboardSummary(openJobs, activeApplications, totalCandidates,
            timeToHire, acceptance, byStage, sources, attention, activity, health);
    }
}
```

Note the offer-acceptance denominator is a pragmatic proxy (applications that saw any progression in
the window). The spec's exact "reached an offer-position stage" needs per-pipeline offer-stage
identification; if a reviewer wants the precise definition, compute the set of last-non-terminal
stage ids per template and count distinct applications whose events touched them. Ship the proxy now,
label it in a code comment, and leave the refinement as a follow-up rather than blocking the screen.

- [ ] **Step 3: Rebuild `Views/Dashboard/Index.cshtml`**

Prototype L126-270. Four `_StatTile`s, the pipeline distribution card with source split beneath, the
needs-you list, the dark ReferralTool health card, and the activity feed. Full view:

```razor
@model Ats.Application.Dashboard.DashboardSummary
@using Ats.Web.Models.Shared
@using Ats.Domain.Enums
@{
    ViewData["Title"] = "Dashboard";
    ViewData["Eyebrow"] = DateTimeOffset.Now.ToString("dddd d MMMM") + ":";
    var maxStage = Model.ByStage.Count == 0 ? 1 : Math.Max(1, Model.ByStage.Max(s => s.Count));
    var ramp = new[] { "var(--no-stage-1)", "var(--no-stage-2)", "var(--no-stage-3)", "var(--no-stage-4)", "var(--no-stage-5)" };
    string SourceLabel(ApplicationOrigin o) => o switch {
        ApplicationOrigin.CareerSite => "Career site", ApplicationOrigin.Referral => "ReferralTool",
        ApplicationOrigin.Manual => "Added manually", _ => "Other" };
}

<div class="row g-3 mb-3">
    <div class="col-sm-6 col-xl-3"><partial name="Partials/_StatTile" model="new StatTileModel(&quot;Open jobs:&quot;, Model.OpenJobs.ToString())" /></div>
    <div class="col-sm-6 col-xl-3"><partial name="Partials/_StatTile" model="new StatTileModel(&quot;Active applications:&quot;, Model.ActiveApplications.ToString(), null, Model.TotalCandidates + &quot; candidates total&quot;, &quot;group&quot;)" /></div>
    <div class="col-sm-6 col-xl-3"><partial name="Partials/_StatTile" model="new StatTileModel(&quot;Time to hire:&quot;, Model.TimeToHireDays?.ToString() ?? &quot;—&quot;, Model.TimeToHireDays is null ? null : &quot; days&quot;)" /></div>
    <div class="col-sm-6 col-xl-3"><partial name="Partials/_StatTile" model="new StatTileModel(&quot;Offer acceptance:&quot;, Model.OfferAcceptanceRate?.ToString() ?? &quot;—&quot;, Model.OfferAcceptanceRate is null ? null : &quot;%&quot;)" /></div>
</div>

<div class="row g-3 align-items-start">
    <div class="col-lg-7">
        <div class="ats-card">
            <span class="ats-eyebrow">Across all open jobs:</span>
            <h2 class="mt-1 mb-3">Pipeline right now.</h2>
            @if (Model.ByStage.Count == 0)
            {
                <partial name="Partials/_EmptyState" model="new EmptyStateModel(&quot;view_week&quot;, &quot;No active applications yet.&quot;)" />
            }
            else
            {
                <div class="d-flex flex-column gap-2">
                    @for (var i = 0; i < Model.ByStage.Count; i++)
                    {
                        var s = Model.ByStage[i];
                        <div class="ats-pipebar-row">
                            <span class="ats-pipebar-row-label">@s.Stage</span>
                            <span class="ats-pipebar-row-track"><span class="ats-pipebar-row-fill" style="width:@(s.Count * 100 / maxStage)%;background:@ramp[i % ramp.Length]"></span></span>
                            <span class="ats-pipebar-row-count">@s.Count</span>
                        </div>
                    }
                </div>
            }
            @if (Model.Sources.Count > 0)
            {
                <div class="ats-stat-strip mt-3 pt-3" style="border-top:1px solid var(--ats-border-subtle)">
                    @foreach (var src in Model.Sources)
                    {
                        <span><span class="ats-eyebrow">@SourceLabel(src.Origin)</span><span class="ats-stat-mini">@src.Percent%</span></span>
                    }
                </div>
            }
        </div>
    </div>

    <div class="col-lg-5 d-flex flex-column gap-3">
        <div class="ats-card">
            <span class="ats-eyebrow">Needs you:</span>
            <h2 class="mt-1 mb-2">Open loops.</h2>
            @if (Model.NeedsAttention.Count == 0)
            {
                <p class="ats-small ats-muted mb-0">Nothing needs you right now.</p>
            }
            else
            {
                <div class="d-flex flex-column">
                    @foreach (var item in Model.NeedsAttention)
                    {
                        var tone = item.Tone switch { "danger" => "ats-pill--danger", "warning" => "ats-pill--warning", _ => "ats-pill--info" };
                        <a href="@item.Url" class="d-flex align-items-center gap-3 py-2 px-1" style="color:var(--ats-ink);border-radius:var(--no-radius-md)">
                            <span class="ats-pill @tone" style="width:2rem;height:2rem;padding:0;justify-content:center;border-radius:var(--no-radius-sm)"><span class="ms ms-sm">@item.Icon</span></span>
                            <span class="ats-cell-stack flex-grow-1">
                                <span class="ats-small">@item.Headline</span>
                                <span class="ats-xsmall ats-muted">@item.Subline</span>
                            </span>
                            <span class="ms ms-sm ats-faint">chevron_right</span>
                        </a>
                    }
                </div>
            }
        </div>

        <div class="ats-card-dark">
            <div class="d-flex align-items-center gap-2 mb-2">
                <span class="ats-board-col-dot" style="background:@(Model.Integration.Connected ? "var(--no-medium-aqua)" : "var(--no-roman-silver)")"></span>
                <span class="ats-eyebrow">ReferralTool · @(Model.Integration.Connected ? "connected" : "off")</span>
            </div>
            <p class="ats-small mb-3" style="color:#D6DDE5">
                @(Model.Integration.FeedLastPulledAt is null
                    ? "No vacancy feed pull recorded yet."
                    : $"Vacancy feed pulled {Ats.Application.Common.RelativeTime.Long(Model.Integration.FeedLastPulledAt, DateTimeOffset.UtcNow)}.")
                @Model.Integration.Delivered24h delivered, @Model.Integration.Failed24h failed, @Model.Integration.Pending pending.
            </p>
            <a asp-controller="Integration" asp-action="Index" class="btn btn-outline-light btn-sm">
                Integration health <span class="ms ms-sm">arrow_forward</span>
            </a>
        </div>
    </div>
</div>

<div class="ats-card mt-3">
    <span class="ats-eyebrow">Latest activity:</span>
    <h2 class="mt-1 mb-2">What moved.</h2>
    @if (Model.Activity.Count == 0)
    {
        <p class="ats-small ats-muted mb-0">No activity recorded yet.</p>
    }
    else
    {
        <div class="d-flex flex-column">
            @foreach (var a in Model.Activity)
            {
                <div class="d-flex align-items-center gap-3 py-2" style="border-bottom:1px solid var(--ats-border-subtle)">
                    <partial name="Partials/_Avatar" model="new AvatarModel(a.Actor, 2)" />
                    <span class="ats-small flex-grow-1">@a.Text</span>
                    <span class="ats-mono ats-xsmall ats-muted">@a.Time</span>
                </div>
            }
        </div>
    }
</div>
```

- [ ] **Step 4: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild dashboard with real metrics`*

---

## Task 10: Verify end to end

Requires the developer signed in (as in the Phase 1 walkthrough). The reviewer cannot log in.

- [ ] **Step 1: Build + tests**

Run: `dotnet build` then `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: build clean; all tests pass (Phase 1's 52 + Task 1's new cases).

- [ ] **Step 2: Run and walk the screens** (developer, signed in)

`dotnet run --project src/Ats.Web`, then confirm at desktop width (>1200px):

1. **Dashboard** — four KPI tiles (open jobs, active applications, time-to-hire showing a number or
   `—`, offer acceptance or `—`); pipeline bars in stage order with the source-split strip beneath;
   needs-you list linking to the right places; dark ReferralTool card showing the feed-pull age (or
   "No vacancy feed pull recorded yet." since Phase 3 hasn't wired the write); activity feed.
2. **Jobs** — filter pills switch the list and preserve the search term; each row shows the pipeline
   mini-bar with the "N applied · N screening" subline and an avatar stack; the `more_horiz` menu
   still publishes/closes/edits/deletes; row click opens the board; pager works.
3. **Board** — header with status pill + meta + tabs + stats strip; cards show avatar, email, source
   chip, an age chip that turns amber past 3 days and red past 7, and stage progress dots; drag a card
   between columns and confirm it persists on reload; the move-select fallback still works; the empty
   Hired column shows the dashed drop hint.
4. **Card click opens the drawer** — slides in from the right, shows stage progress, application facts
   (source chip, referral code only when present, delivery pill only when an outbox message exists),
   the CV card with a real file size, and the history timeline; closes on backdrop/Escape/close.
5. **Candidates** — avatar + name + latest job, two-line contact, source chip, latest stage, last
   activity relative time; the row `more_horiz` still adds to a job; pager works.
6. **Application deep link** — browse directly to `/Applications/Details/{id}`; the same drawer body
   renders full-page (no-JS fallback) with a Board back button.

- [ ] **Step 3: Confirm origin stamping** (developer)

Apply through the public career site with a referral code (`/careers/{slug}/jobs/{ref}?<codeparam>=RT-1`)
→ the new board card shows a **Referral** chip. Apply without a code → **Career site**. Add a
candidate via the board → **Manual**. Rows created before Phase 2 still show **Not recorded**.

- [ ] **Step 4: Confirm nothing regressed**

The move concurrency warning still appears if two tabs move the same card; publish/close still drive
the career site; the delivery log and audit log are unchanged.

*Commit point: `test: verify phase 2 screens and origin stamping`*

---

## Task 11: Documentation

**Files:**
- Modify: `.claude/skills/entities/SKILL.md`, `.claude/skills/pipeline/SKILL.md`, `.claude/skills/audit/SKILL.md`, `.claude/skills/ui/SKILL.md`

- [ ] **Step 1: Record the read models**

- `entities`: note `Origin` is now stamped at creation (Manual / CareerSite / Referral) and list the
  three read-model query services (`IJobListQuery`, `ICandidateListQuery`, `IApplicationCardQuery`)
  and where they live.
- `pipeline`: note the board card now shows source, days-in-stage and progress, and that the move
  flow (SortableJS + htmx + RowVersion) is unchanged.
- `audit`: note the dashboard now surfaces an activity feed derived from `AuditEntry` and a
  needs-attention list.
- `ui`: note the candidate drawer pattern (htmx GET → `_CandidateDrawer` partial → `#ats-drawer-host`,
  wrapped in the overlay by `site.js`) and that `Applications/Details` reuses the same partial.

- [ ] **Step 2: Verify**

Re-read each edited section; confirm no statement contradicts the built code.

*Commit point: `docs: update skills for phase 2 read models and drawer`*

---

## Phase 2 exit criteria

- [ ] `dotnet build` clean; `dotnet test` green.
- [ ] Dashboard, Jobs, Board, Candidates rebuilt to the prototype layout; drawer opens from board cards.
- [ ] Time-to-hire, offer acceptance, source split, per-job stage counts, board stats strip and
      days-in-stage all render from real data, with honest `—`/empty states when there is nothing to show.
- [ ] `Origin` is stamped on new applications and shown as source chips; old rows read "Not recorded".
- [ ] Board drag + dropdown move still persist and still surface the concurrency warning.
- [ ] No new migration; no change to the outbox/worker/feed/ReferralTool contract.

Deferred to Phase 3: pipelines editor, organisation, integrations, audit, career-site back office +
branding screen, and wiring the `FeedLastPulledAt` write.
