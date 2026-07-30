# ATS NowOnline Redesign - Phase 4: Public career site

> **For agentic workers:** checkbox (`- [ ]`) steps, worked in order.
>
> **Repo constraints (unchanged):** no `git` commands (each task ends with Verify; commit points are
> marked for the developer) and no `dotnet ef database update`. **Phase 4 adds no migration and no new
> tests** (it is view-layer work over existing, already-tested services).
>
> **Visual source of truth:** `ATS - Redesign.dc.html`, the career-site block L907-968 (browser-frame
> hero + filter pills + role cards) and the hero treatment described in the design-system README
> (Oxford-Blue hero, two blurred blobs, headline with one outlined line, sentence-case eyebrow).
> Component + token classes already exist in `wwwroot/css/ats-*.css`; Phase 4 adds a small
> `ats-careers.css` for the public-only hero/blob/footer pieces (this file was named in the Phase 1
> spec but not yet created).

**Goal:** Rebuild the public career site (`Careers` area) — landing hero, department filter, role
cards, job detail + apply form, thank-you — into the NowOnline design, reading the per-tenant
branding (accent, tenant name, hero copy) that the Phase 3 editor writes. This is the last redesign
surface.

**Architecture:** View-layer only plus thin controller wiring. The careers `JobsController` gets the
resolved `TenantBranding` from `ITenantBrandingService` (which resolves the tenant from the slug, so
it works on anonymous public requests) and passes it to the views via small view models. The layout
emits the tenant accent with the existing `<vc:branding>` view component (public CTAs pick up the
tenant colour). Department filtering is client-side over the already-loaded job list (small,
single-tenant published set) — no new controller params, matching the prototype's instant feel. Every
existing behaviour — slug tenancy, referral-code capture cookie, resume validation, the apply POST and
its antiforgery, ThankYou redirect — is preserved untouched; only markup and the two view models change.

**Tech Stack:** unchanged.

**Spec:** `docs/specs/2026-07-30-ats-nowonline-redesign-design.md`
**Prior phases:** phases 1-3 (all committed).

---

## File Structure

### Created

| File | Responsibility |
|---|---|
| `src/Ats.Web/wwwroot/css/ats-careers.css` | public hero, blobs, outlined headline, role card, footer |
| `src/Ats.Web/Areas/Careers/Models/CareerIndexViewModel.cs` | branding + jobs + slug for the landing page |

### Modified

| File | Change |
|---|---|
| `src/Ats.Web/Areas/Careers/Controllers/JobsController.cs` | inject branding; pass it to Index/Detail/ThankYou; set `ViewData` for the layout |
| `src/Ats.Web/Areas/Careers/Views/Shared/_CareersLayout.cshtml` | rebuilt nav + footer + `<vc:branding>` + `ats-careers.css` |
| `src/Ats.Web/Areas/Careers/Views/Jobs/Index.cshtml` | rebuilt (hero + filter pills + role cards) |
| `src/Ats.Web/Areas/Careers/Views/Jobs/Detail.cshtml` | rebuilt (job header + apply form) |
| `src/Ats.Web/Areas/Careers/Views/Jobs/ThankYou.cshtml` | rebuilt |
| `src/Ats.Web/Areas/Careers/Views/_ViewImports.cshtml` | add usings if needed for the model/branding |

---

## Task 1: Careers stylesheet

**Files:**
- Create: `src/Ats.Web/wwwroot/css/ats-careers.css`

- [ ] **Step 1: Write the public-only pieces**

Everything else (buttons, cards, chips, pills) reuses `ats-components.css`. This file adds only what
is unique to the public site: the dark hero, the blurred blobs, the outlined-headline helper, the
role-card hover, and the footer.

```css
/* Public career site — hero, blobs, role cards, footer. Depends on ats-tokens.css + ats-base.css. */

.careers-nav {
  background: var(--ats-surface);
  border-bottom: 1px solid var(--ats-border);
}
.careers-nav-inner {
  max-width: 1058px; margin: 0 auto; padding: 1.25rem 1.5rem;
  display: flex; align-items: center; gap: .75rem;
}

.careers-hero {
  position: relative;
  overflow: hidden;
  background: linear-gradient(160deg, var(--no-oxford-blue), var(--no-maastricht-blue));
  color: #fff;
}
.careers-hero-inner {
  position: relative; z-index: 1;
  max-width: 1058px; margin: 0 auto; padding: 4.5rem 1.5rem;
}
.careers-hero-blob {
  position: absolute; border-radius: 50%; filter: blur(10px); pointer-events: none;
}
.careers-hero-blob--1 { width: 520px; height: 420px; background: var(--ats-accent); opacity: .34; right: -120px; top: -140px; }
.careers-hero-blob--2 { width: 380px; height: 320px; background: var(--no-medium-aqua); opacity: .26; right: 140px; bottom: -180px; }
.careers-hero-eyebrow { color: #8FA0B4; }
.careers-hero-title {
  font-family: var(--no-font-display); font-weight: 800;
  font-size: clamp(2.5rem, 6vw, 3.5rem); line-height: 1.12; letter-spacing: -.01em;
  margin: 1rem 0; max-width: 40rem;
}
/* One outlined line inside the headline (the signature NowOnline treatment). */
.careers-hero-title .outlined { color: transparent; -webkit-text-stroke: 1.5px #fff; }
.careers-hero-intro {
  font-size: 1.0625rem; font-weight: 300; line-height: 1.56; color: #C6D0DA; max-width: 40rem;
}

.careers-body { max-width: 1058px; margin: 0 auto; padding: 2.5rem 1.5rem 4rem; }

.careers-roles { display: grid; grid-template-columns: 1fr 1fr; gap: .875rem; }
@media (max-width: 767.98px) { .careers-roles { grid-template-columns: 1fr; } }

.careers-role-card {
  display: flex; flex-direction: column; gap: .75rem;
  border: 1px solid var(--ats-border); border-radius: var(--no-radius-lg);
  padding: 1.375rem; background: var(--ats-surface); color: var(--ats-ink);
  transition: box-shadow .15s ease, transform .15s ease;
}
.careers-role-card:hover { box-shadow: var(--no-shadow-lg); transform: translateY(-1px); color: var(--ats-ink); }
.careers-role-title { font-family: var(--no-font-display); font-weight: 800; font-size: 1.25rem; letter-spacing: -.01em; }
.careers-role-cta {
  display: inline-flex; align-items: center; gap: .375rem;
  font-family: var(--no-font-display); font-weight: 600; font-size: .84375rem; color: var(--ats-accent);
}

/* Filter pills specific to the public site (the back-office .ats-filter-group is a segmented control;
   here the pills are standalone, matching the prototype). */
.careers-filter { display: flex; align-items: center; gap: .625rem; flex-wrap: wrap; }
.careers-filter-pill {
  border: 1px solid var(--ats-border); background: var(--ats-surface); color: var(--ats-ink-muted);
  border-radius: var(--no-radius-pill); padding: .4375rem 1rem; font-size: .8125rem; cursor: pointer;
}
.careers-filter-pill.active { background: var(--no-oxford-blue); color: #fff; border-color: var(--no-oxford-blue); }

.careers-detail { max-width: 788px; margin: 0 auto; padding: 2.5rem 1.5rem 4rem; }

.careers-footer {
  background: var(--no-oxford-blue); color: #fff; margin-top: 3rem;
}
.careers-footer-inner {
  max-width: 1058px; margin: 0 auto; padding: 2.5rem 1.5rem;
  display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap;
}
```

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: success (CSS is not compiled; this just confirms nothing else broke). Visual check in Task 6.

*Commit point: `feat: public career-site stylesheet`*

---

## Task 2: Controller — pass branding to the public views

**Files:**
- Create: `src/Ats.Web/Areas/Careers/Models/CareerIndexViewModel.cs`
- Modify: `src/Ats.Web/Areas/Careers/Controllers/JobsController.cs`

- [ ] **Step 1: Landing view model**

`src/Ats.Web/Areas/Careers/Models/CareerIndexViewModel.cs`:

```csharp
using Ats.Application.Branding;
using Ats.Domain.Entities;

namespace Ats.Web.Areas.Careers.Models;

public class CareerIndexViewModel
{
    public TenantBranding Branding { get; set; } = default!;
    public string Slug { get; set; } = "";
    public IReadOnlyList<Job> Jobs { get; set; } = new List<Job>();
}
```

- [ ] **Step 2: Inject branding and enrich the actions**

Add `ITenantBrandingService` to the careers `JobsController`. It resolves the tenant from the slug
(set by `TenantResolutionMiddleware`), so it works on these anonymous requests exactly as it does in
the back office. Preserve every existing line — referral capture, resume validation, apply, redirect;
only add branding plumbing.

Constructor:

```csharp
    private readonly ICareerService _career;
    private readonly IFileStore _files;
    private readonly ITenantBrandingService _branding;

    public JobsController(ICareerService career, IFileStore files, ITenantBrandingService branding)
    {
        _career = career; _files = files; _branding = branding;
    }
```

Add `using Ats.Application.Branding;`.

`Index` — return the new view model, and set the tenant name for the layout:

```csharp
    [HttpGet("")]
    public async Task<IActionResult> Index(string slug)
    {
        ViewData["Title"] = "Open positions";
        ViewData["Slug"] = slug;
        ResolveReferralCode(slug, await _career.GetCodeParameterNameAsync()); // capture ?ref on the landing page
        var branding = await _branding.GetAsync();
        ViewData["CareerTenantName"] = branding.TenantName;
        return View(new CareerIndexViewModel
        {
            Branding = branding,
            Slug = slug,
            Jobs = await _career.GetPublishedJobsAsync()
        });
    }
```

`Detail` — the existing `CareerJobDetailViewModel` stays; just set the tenant name for the layout.
After computing `codeParam`/before returning, add `ViewData["CareerTenantName"] = (await _branding.GetAsync()).TenantName;`.
Do the same in the `RedisplayAsync` local inside `Apply`, and in `ThankYou`. A concise way: add a
tiny private helper and call it at the top of each action:

```csharp
    private async Task SetLayoutBrandingAsync()
        => ViewData["CareerTenantName"] = (await _branding.GetAsync()).TenantName;
```

Call `await SetLayoutBrandingAsync();` at the start of `Index` (replacing the inline set above is
fine), `Detail`, `ThankYou`, and inside `RedisplayAsync`. `GetAsync` is request-cached, so the
repeated calls are one query per request.

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: pass tenant branding to the public career site`*

---

## Task 3: Rebuild the careers layout

**Files:**
- Modify: `src/Ats.Web/Areas/Careers/Views/_ViewImports.cshtml`
- Modify: `src/Ats.Web/Areas/Careers/Views/Shared/_CareersLayout.cshtml`

- [ ] **Step 0: Register the view-component tag helper in the area (REQUIRED)**

The area's `_ViewImports.cshtml` currently has only `@using Ats.Web.Areas.Careers.Models`,
`@using Ats.Domain.Entities`, and `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers`. It does NOT
register the `Ats.Web` assembly's tag helpers, so `<vc:branding>` would emit as inert literal markup
and the tenant accent would silently not apply. Add:

```razor
@using Ats.Web.Areas.Careers.Models
@using Ats.Domain.Entities
@using Ats.Application.Branding
@using Ats.Web.Models.Shared
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Ats.Web
```

(`Ats.Web.Models.Shared` is for `_EmptyState`'s `EmptyStateModel`; `Ats.Application.Branding` for the
landing view model's `TenantBranding`. Shared partials resolve by short name via the default
area→root `Views/Shared` fallback, so no tilde paths are needed after all.)

- [ ] **Step 1: Rebuild**

Add `ats-careers.css`, emit the tenant accent with `<vc:branding>` (so CTAs use the tenant colour),
put the tenant name in the nav and footer (from `ViewData["CareerTenantName"]`, falling back to
"Careers"), and keep the jquery-validation scripts for the apply form. The nav and footer are full
width; page content supplies its own centered container.

```razor
@{
    var tenantName = ViewData["CareerTenantName"] as string ?? "Careers";
    var year = ViewData["Year"] as int? ?? 0;
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] · @tenantName</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E%3Crect width='16' height='16' rx='3' fill='%230085CA'/%3E%3Cpath d='M5 5V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1h2v7H3V5h2zm1 0h4V4H6v1z' fill='white'/%3E%3C/svg%3E" />
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/material-symbols/outlined.css" />
    <link rel="stylesheet" href="~/css/ats-tokens.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-base.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-components.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-careers.css" asp-append-version="true" />
    @* Emits --ats-accent for this tenant so public CTAs pick up the brand colour. *@
    <vc:branding></vc:branding>
</head>
<body>
    <nav class="careers-nav">
        <div class="careers-nav-inner">
            <span class="ats-brand-mark">@(string.IsNullOrEmpty(tenantName) ? "A" : tenantName.Trim()[..1])</span>
            <span style="font-family:var(--no-font-display);font-weight:800;letter-spacing:-.01em">@tenantName</span>
            <span class="ats-brand-sub ms-1">careers</span>
        </div>
    </nav>

    @RenderBody()

    <footer class="careers-footer">
        <div class="careers-footer-inner">
            <span style="font-family:var(--no-font-display);font-weight:800;font-size:1.25rem;letter-spacing:-.01em">@tenantName</span>
            <span class="ats-small" style="color:#8FA0B4">Powered by ATS · when you know, you know.</span>
        </div>
    </footer>

    <script src="~/lib/jquery/dist/jquery.min.js"></script>
    <script src="~/lib/jquery-validation/dist/jquery.validate.min.js"></script>
    <script src="~/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

The `tenantName.Trim()[..1]` brand-mark initial is safe because `tenantName` defaults to "Careers"
(never empty). If a tenant name could be whitespace, the `string.IsNullOrEmpty` guard already covers
the fallback letter.

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild careers layout`*

---

## Task 4: Rebuild the landing page

**Files:**
- Modify: `src/Ats.Web/Areas/Careers/Views/Jobs/Index.cshtml`

- [ ] **Step 1: Rebuild**

Hero (Oxford-Blue, two blobs, eyebrow, headline with the second line outlined, intro — all from
branding with sensible defaults), then the body: department filter pills + role cards. Filtering is
client-side over `data-dept`. The role card links to `Detail` exactly as today
(`asp-route-slug`/`asp-route-externalRef`), so referral capture and routing are unchanged.

```razor
@model Ats.Web.Areas.Careers.Models.CareerIndexViewModel
@{
    Layout = "_CareersLayout";
    var b = Model.Branding;
    var headline = string.IsNullOrWhiteSpace(b.CareerHeroHeadline) ? "Build things that" : b.CareerHeroHeadline;
    var outlined = string.IsNullOrWhiteSpace(b.CareerHeroHeadlineOutlined) ? "actually ship." : b.CareerHeroHeadlineOutlined;
    var intro = string.IsNullOrWhiteSpace(b.CareerHeroIntro)
        ? "Open roles, one team, no noise. Find the one that fits."
        : b.CareerHeroIntro;

    var depts = Model.Jobs
        .Select(j => j.Department?.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n)
        .ToList();
}

<header class="careers-hero">
    <span class="careers-hero-blob careers-hero-blob--1"></span>
    <span class="careers-hero-blob careers-hero-blob--2"></span>
    <div class="careers-hero-inner">
        <span class="ats-eyebrow careers-hero-eyebrow">@b.TenantName · careers</span>
        <h1 class="careers-hero-title">@headline<br /><span class="outlined">@outlined</span></h1>
        <p class="careers-hero-intro">@intro</p>
    </div>
</header>

<main class="careers-body">
    @if (Model.Jobs.Count == 0)
    {
        <partial name="Partials/_EmptyState" model="@(new Ats.Web.Models.Shared.EmptyStateModel("work_outline", "No open positions right now.", "Please check back later."))" />
    }
    else
    {
        <div class="careers-filter mb-4">
            <button type="button" class="careers-filter-pill active" data-filter="*">All roles</button>
            @foreach (var d in depts)
            {
                <button type="button" class="careers-filter-pill" data-filter="@d">@d</button>
            }
            <span class="ms-auto ats-mono ats-xsmall ats-muted">@Model.Jobs.Count open @(Model.Jobs.Count == 1 ? "position" : "positions")</span>
        </div>

        <div class="careers-roles">
            @foreach (var j in Model.Jobs)
            {
                <a class="careers-role-card" data-dept="@(j.Department?.Name ?? "")"
                   asp-controller="Jobs" asp-action="Detail" asp-route-slug="@Model.Slug" asp-route-externalRef="@j.ExternalRef">
                    <span class="careers-role-title">@j.Title</span>
                    <span class="d-flex gap-2 flex-wrap">
                        <span class="ats-chip ats-chip--neutral">@j.EmploymentType</span>
                        @if (j.Location is not null) { <span class="ats-chip ats-chip--neutral">@(j.Location.City ?? j.Location.Name)</span> }
                        @if (j.Department is not null) { <span class="ats-chip ats-chip--neutral">@j.Department.Name</span> }
                    </span>
                    <span class="careers-role-cta">Bekijk vacature <span class="ms ms-sm">arrow_forward</span></span>
                </a>
            }
        </div>
    }
</main>

@section Scripts {
    <script>
        // Client-side department filter over the already-loaded role cards.
        (function () {
            const pills = document.querySelectorAll('.careers-filter-pill');
            const cards = document.querySelectorAll('.careers-role-card');
            pills.forEach(function (pill) {
                pill.addEventListener('click', function () {
                    pills.forEach(p => p.classList.remove('active'));
                    pill.classList.add('active');
                    const f = pill.getAttribute('data-filter');
                    cards.forEach(function (card) {
                        card.style.display = (f === '*' || card.getAttribute('data-dept') === f) ? '' : 'none';
                    });
                });
            });
        })();
    </script>
}
```

`_EmptyState` (in `Views/Shared/Partials`) resolves by the short name `Partials/_EmptyState` from the
Careers area via the default area→root `Views/Shared` fallback — confirmed against the view-location
config. No tilde path needed.

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild public careers landing page`*

---

## Task 5: Rebuild the job detail + apply form

**Files:**
- Modify: `src/Ats.Web/Areas/Careers/Views/Jobs/Detail.cshtml`

- [ ] **Step 1: Rebuild**

Model is the existing `CareerJobDetailViewModel` (unchanged). Keep the form's action, method,
`enctype`, the hidden `SourceCode`, the file input's accept/validation hooks, and the
`asp-validation` spans exactly — only restyle. A small dark header band for the job title, then the
description, then the apply card.

```razor
@model Ats.Web.Areas.Careers.Models.CareerJobDetailViewModel
@{
    Layout = "_CareersLayout";
}
<header class="careers-hero">
    <span class="careers-hero-blob careers-hero-blob--1"></span>
    <div class="careers-hero-inner">
        <a asp-controller="Jobs" asp-action="Index" asp-route-slug="@Model.Slug"
           class="ats-eyebrow careers-hero-eyebrow d-inline-flex align-items-center gap-1" style="text-decoration:none">
            <span class="ms ms-sm">arrow_back</span> All positions
        </a>
        <h1 class="careers-hero-title" style="font-size:clamp(2rem,5vw,2.75rem)">@Model.Job.Title</h1>
        <span class="d-flex gap-2 flex-wrap">
            <span class="ats-chip ats-chip--neutral">@Model.Job.EmploymentType</span>
            @if (Model.Job.Location is not null) { <span class="ats-chip ats-chip--neutral">@(Model.Job.Location.City ?? Model.Job.Location.Name)</span> }
            @if (Model.Job.Department is not null) { <span class="ats-chip ats-chip--neutral">@Model.Job.Department.Name</span> }
        </span>
    </div>
</header>

<main class="careers-detail">
    @if (!string.IsNullOrWhiteSpace(Model.Job.Description))
    {
        <div class="no-body mb-4" style="white-space:pre-wrap;line-height:1.56">@Model.Job.Description</div>
    }

    <div class="ats-card">
        <h2 class="mb-3">Apply for this role.</h2>
        @if (!string.IsNullOrEmpty(Model.Error))
        {
            <div class="alert alert-danger d-flex align-items-center gap-2" role="alert">
                <span class="ms ms-sm">error</span> @Model.Error
            </div>
        }
        <form asp-controller="Jobs" asp-action="Apply" asp-route-slug="@Model.Slug"
              asp-route-externalRef="@Model.Job.ExternalRef" method="post" enctype="multipart/form-data"
              class="d-flex flex-column gap-3">
            <div asp-validation-summary="ModelOnly" class="text-danger small"></div>
            <input type="hidden" asp-for="Code" name="SourceCode" />
            <div class="row g-3">
                <label class="col-md-6 d-flex flex-column gap-1">
                    <span class="form-label mb-0">First name</span>
                    <input asp-for="FirstName" class="form-control" required />
                    <span asp-validation-for="FirstName" class="text-danger small"></span>
                </label>
                <label class="col-md-6 d-flex flex-column gap-1">
                    <span class="form-label mb-0">Last name</span>
                    <input asp-for="LastName" class="form-control" required />
                    <span asp-validation-for="LastName" class="text-danger small"></span>
                </label>
                <label class="col-md-6 d-flex flex-column gap-1">
                    <span class="form-label mb-0">Email</span>
                    <input asp-for="Email" type="email" class="form-control" required />
                    <span asp-validation-for="Email" class="text-danger small"></span>
                </label>
                <label class="col-md-6 d-flex flex-column gap-1">
                    <span class="form-label mb-0">Phone</span>
                    <input asp-for="Phone" class="form-control" />
                    <span asp-validation-for="Phone" class="text-danger small"></span>
                </label>
            </div>
            <label class="d-flex flex-column gap-1">
                <span class="form-label mb-0">Resume (PDF or Word, max 5 MB)</span>
                <input name="resume" type="file" accept=".pdf,.doc,.docx" class="form-control" required />
            </label>
            <div>
                <button type="submit" class="btn btn-primary">Submit application <span class="ms ms-sm">arrow_forward</span></button>
            </div>
            <p class="ats-xsmall ats-muted mb-0">A resume in PDF or Word format is required.</p>
        </form>
    </div>
</main>
```

**No `@section Scripts` here.** `_CareersLayout` already loads jquery + jquery-validation +
jquery-validation-unobtrusive globally for every careers page, so client-side validation is already
wired; adding `_ValidationScriptsPartial` would load those two scripts a second time. The current
careers Detail view relies on the layout's global scripts the same way.

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild public job detail and apply form`*

---

## Task 6: Rebuild the thank-you page

**Files:**
- Modify: `src/Ats.Web/Areas/Careers/Views/Jobs/ThankYou.cshtml`

- [ ] **Step 1: Rebuild**

```razor
@{
    Layout = "_CareersLayout";
    var slug = ViewData["Slug"] as string;
}
<main class="careers-detail" style="text-align:center">
    <span class="ms" style="font-size:3rem;color:var(--no-success)">check_circle</span>
    <h1 class="mt-3 mb-2">Application received.</h1>
    <p class="no-intro ats-muted mb-4">Thanks for applying. We will be in touch.</p>
    <a class="btn btn-outline-secondary" asp-controller="Jobs" asp-action="Index" asp-route-slug="@slug">
        <span class="ms ms-sm">arrow_back</span> Back to open positions
    </a>
</main>
```

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: rebuild careers thank-you page`*

---

## Task 7: Verify end to end

The public site is anonymous — no login needed, so this is fully checkable in the browser.

- [ ] **Step 1: Build + tests**

`dotnet build`; `dotnet test tests/Ats.Tests/Ats.Tests.csproj` — build clean, 68 tests still green.

- [ ] **Step 2: Walk the public site** (`dotnet run --project src/Ats.Web`)

1. **Landing** — browse `/careers/<slug>` (a slug from the DB, e.g. the one shown on the back-office
   Career site screen). Confirm the Oxford-Blue hero with two blurred blobs, the eyebrow
   "<Tenant> · careers", the headline with the second line outlined (stroke, transparent fill), and
   the intro. With default branding it reads "Build things that / actually ship."; after setting hero
   copy in the back-office Branding editor, it reflects that copy. The accent on CTAs matches the
   tenant's brand accent.
2. **Filter** — the department pills filter the role cards instantly; "All roles" restores them; the
   open-position count is right.
3. **Role card → detail** — clicking a card opens the job detail with the restyled header + chips and
   the description; the apply card renders.
4. **Apply (happy path)** — fill the form, attach a small PDF, submit → lands on the restyled
   thank-you; the application appears on the back-office board with the correct source chip
   (Career site, or Referral if a `?<codeparam>=CODE` was on the URL — verify both).
5. **Apply (validation)** — submit with a missing field or a non-PDF/oversized file → the form
   re-renders with the error styled, and the entered values preserved.
6. **Referral capture** — open `/careers/<slug>?<codeparam>=RT-1`, navigate to a job, apply without a
   code on the job URL → the resulting application is stamped Referral with the code (cookie capture
   still works).
7. **Unknown / suspended slug** — `/careers/does-not-exist` still 404s (tenant resolution unchanged).
8. **Responsive** — narrow the window: role cards collapse to one column, the hero scales, nothing
   overflows horizontally.

- [ ] **Step 3: Confirm no regression**

The apply POST still validates and stores the resume, still redirects to ThankYou, still stamps
`Origin`; the referral cookie scope/behaviour is unchanged; only Published jobs are exposed.

*Commit point: `test: verify public career site`*

---

## Task 8: Documentation

**Files:**
- Modify: `.claude/skills/career-site/SKILL.md`, `.claude/skills/ui/SKILL.md`, `CLAUDE.md`

- [ ] **Step 1: Record the changes**

- `career-site`: the public site is now the NowOnline design — hero reads the tenant's branding copy
  (`CareerHeroHeadline`/`Outlined`/`Intro`) and accent (via `<vc:branding>` in `_CareersLayout`);
  department filtering is client-side; the apply flow, referral cookie, resume validation and slug
  tenancy are unchanged.
- `ui`: note `ats-careers.css` as the fifth stylesheet (public-site-only: hero, blobs, role cards,
  footer) and that `_CareersLayout` now emits `<vc:branding>`.
- `CLAUDE.md`: if the doc lists redesign phases or surfaces, mark the public career site done — the
  redesign is complete across back office + public site.

- [ ] **Step 2: Verify** — re-read each edited section against the built code.

*Commit point: `docs: update skills for the public career-site redesign`*

---

## Phase 4 exit criteria

- [ ] `dotnet build` clean; `dotnet test` green (68 tests, unchanged — view-layer phase).
- [ ] Public landing, detail, apply and thank-you rebuilt to the NowOnline design.
- [ ] Hero reflects the tenant's branding copy + accent; department filter works client-side.
- [ ] Apply flow, referral-code capture, resume validation, slug tenancy and 404s all unchanged.
- [ ] No new migration; ReferralTool contract, outbox and worker untouched.

## Redesign complete

With Phase 4 the whole product — back office (shell, dashboard, jobs, board, candidates, drawer,
pipelines, organisation, integrations, audit, career-site back office + branding) and the public
career site — is on the NowOnline design system. Backend additions across all phases were limited to:
per-tenant branding + application origin + feed-pull timestamp (one migration, Phase 1), and
read-model/query services; no change to tenancy, the ReferralTool contract, the outbox, or the worker.
