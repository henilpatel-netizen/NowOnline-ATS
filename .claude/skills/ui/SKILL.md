---
name: ui
description: The Ats back-office UI pattern - layouts, design tokens, shared components, and the exact steps to add a new page consistently. Read before building or changing any view.
---

# Ats UI

## Stack
Server-rendered ASP.NET Core MVC + Bootstrap 5 (self-hosted in `wwwroot/lib`), reskinned with the
NowOnline design system. Icons: **Material Symbols Outlined**, self-hosted, used as
`<span class="ms">icon_name</span>` (`.ms-sm` / `.ms-lg` / `.ms-xl` size variants). Do NOT use
Bootstrap Icons (`bi-*`) — they were removed; do not mix icon systems. Fonts: Urbanist (display),
Lexend (body), Sometype Mono (eyebrow), self-hosted in `wwwroot/lib/nowonline-fonts`. Client
libraries are in `libman.json`; run `libman restore` to hydrate `wwwroot/lib`. htmx + SortableJS
back the kanban and the global search. No external/CDN requests at runtime.

## Layouts
- `Views/Shared/_Layout.cshtml` - authenticated app shell: `<vc:branding>` (emits per-tenant CSS
  vars), `<vc:sidebar-nav>`, `<vc:top-bar>`, then a scrolling `<main>` that renders `_Alerts`, the
  page head (eyebrow + H1 + optional `PageActions` section, inlined here because sections are not
  legal in a partial), the body, and an empty `#ats-drawer-host` for htmx-loaded drawers.
- `Views/Shared/_AuthLayout.cshtml` - centered card on an Oxford-Blue gradient for anonymous pages.
- `Areas/Careers/Views/Shared/_CareersLayout.cshtml` - public career site.
- Layout selection is declarative via `_ViewStart.cshtml` files (unchanged).

## Design tokens (four layered stylesheets, replacing `site.css`)
Load order matters and is controlled by the `<link>` sequence in each layout:
`ats-tokens.css` (NowOnline `--no-*` tokens, `--ats-*` semantic aliases, Bootstrap variable
overrides) -> `ats-base.css` (`@font-face`, typography, `.ms`, `.ats-eyebrow`) ->
`ats-components.css` (cards, pills, chips, avatars, pipeline bar, tables, kanban, drawer, timeline,
pager, search) -> `ats-shell.css` (sidebar, topbar, content, auth shell), and `ats-careers.css` (public career-site
only: hero, blurred blobs, outlined headline, role cards, footer). `_AuthLayout` loads
tokens/base/components/shell; `_CareersLayout` loads tokens/base/components/careers and emits
`<vc:branding>` for the tenant accent (its area `_ViewImports` registers `@addTagHelper *, Ats.Web`).
- **Views consume `--ats-*` aliases only, never `--no-*`.** The `--no-*` values are ported verbatim
  from the design system's `colors_and_type.css`; re-port rather than hand-tuning.
- No theme colours inline in views. The one sanctioned inline style is the per-tenant accent, and
  that is emitted only by `<vc:branding>` after regex validation.

## Per-tenant branding
`ITenantBrandingService` (Application) resolves accent colour, sidebar theme (dark/light) and career
hero copy from `TenantSettings`, substituting NowOnline defaults for nulls, cached per request.
`BrandingViewComponent` writes the resolved values into a `<style>` block. The accent is validated
by `BrandColor.Normalize` on save and again on emission (it lands in a `style` attribute); anything
invalid falls back to the default.

## Shared components
- `SidebarNavViewComponent` renders grouped nav (ungrouped Dashboard, then `Hiring:`, `Setup:`,
  `Admin:`) from an in-code `NavItem[]`. A `NavItem` carries a `NavGroup`, an optional `RequiredRole`,
  an optional `Count` selector (badge, e.g. open jobs) and an optional `Alert` selector (danger dot).
  Counts and the alert come from `IShellSummaryService` (one cached per-request query batch).
- `TopBarViewComponent` renders the breadcrumb (from a controller->crumb map), the global search
  field (htmx GET to `SearchController`, `Ctrl/Cmd+K` focus), and the notification bell (dot when
  `ShellSummary.HasAttention`). A page adds a primary action by setting `ViewData["TopBarActionText"]`
  + `TopBarActionController` (+ optional `TopBarActionAction`, `TopBarActionIcon`).
- Presentation partials in `Views/Shared/Partials`, all strongly typed via
  `Ats.Web.Models.Shared`: `_Avatar` (deterministic colour + initials from a name),
  `_StatusPill`, `_SourceChip` (application origin), `_StatTile`, `_PipelineBar`, `_EmptyState`,
  `_Timeline`.
- Page head: set `ViewData["Title"]` (drives the H1 and browser title) and optional
  `ViewData["Eyebrow"]` (mono kicker). The trailing heading period ("Jobs.") is added by the layout,
  so `Title` stays "Jobs". There is no `_PageHeader.cshtml` any more.
- `_Alerts.cshtml` renders `TempData["Success"|"Error"|"Info"]` as dismissible alerts with an icon.

## Forms
Use tag helpers: `asp-for`, `asp-validation-for`, `asp-validation-summary="ModelOnly"`. Inputs get
`class="form-control"`, primary action `class="btn btn-primary"`. Client-side validation is wired in
`_AuthLayout`; for back-office forms add `<partial name="_ValidationScriptsPartial" />` in a
`@section Scripts`.

## Add a new back-office page (checklist)
1. Controller action returns `View(...)`; the page is `Views/<Controller>/<Action>.cshtml`.
2. First line: `@{ ViewData["Title"] = "..."; }` (drives the H1 and browser title); optionally
   `ViewData["Eyebrow"] = "...:";` for the mono kicker.
3. Use Bootstrap grid + the `.ats-*` component classes. No inline theme colours; icons via `.ms`.
4. Header buttons: define a `@section PageActions { ... }`, or set the `TopBarAction*` `ViewData`
   keys for a topbar CTA.
5. To surface in the sidebar, append a `NavItem` in `SidebarNavViewComponent` with its `NavGroup`
   (and, if it should be role-gated, `RequiredRole`). Add a matching crumb in `TopBarViewComponent`.
6. For flash messages, set `TempData["Success"]` etc. in the action.

## Security
Antiforgery is validated globally (`AutoValidateAntiforgeryToken`); form tag helpers emit the token.
The auth cookie is `HttpOnly` + `Secure`, so test over https.

## Lists, pagination, and errors (Phase 4)
- Paginated lists use `PagedResult<T>` (`Ats.Application/Common`) + the `_Pager.cshtml` partial
  (`PagerModel` with `Page`, `TotalPages`, `Action`, and a `Query` dictionary of filters to preserve).
  Jobs, Candidates, and the delivery log follow this with a GET search/filter form (page size 20). Build
  the `PagerModel` in a `@{ }` block and pass it as `model`; Razor cannot parse an object initializer
  inline in a tag-helper attribute.
- Error pages: `app.UseStatusCodePagesWithReExecute("/Home/Status/{0}")` renders `HomeController.Status`
  (`Views/Home/Status.cshtml`, neutral `_AuthLayout`) for 404/403; `UseExceptionHandler` renders
  `Views/Shared/Error.cshtml` for 500. The neutral layout serves both back-office and careers visitors.
- Polish: an inline-SVG favicon (Sky-Blue `#0085CA`) in all layouts; `_Alerts` are dismissible;
  `site.js` disables a form's submit button on submit and wires the `Ctrl/Cmd+K` search shortcut.

## Global search
`SearchController` (`GET /Search?q=`) returns the `_Results` partial via htmx into the topbar. Backed
by `IGlobalSearchService` (jobs by title/ExternalRef, candidates by name/email, applications by
referral code), capped 5 per category, tenant-scoped by the global query filter, `LIKE`
metacharacters escaped.

## Organisation + Career site (Phase 3)
Departments and Locations are presented together on `/Organisation` (job counts from
`IOrganisationReadService`); the old `/Departments` and `/Locations` list routes 301-redirect there,
while their create/edit/delete actions and restyled `Form.cshtml` views stay. `CareerSite` is the
back-office career controller (preview + Owner-only Branding); it is deliberately not `Careers`.
When adding a controller whose name could match a literal attribute route (like the public
`careers/{slug}`), pick a non-colliding name — a literal route segment wins over a conventional one.

## Candidate drawer
A right-side overlay used on the board. The board card click issues an htmx GET to
`Applications/Card`, which returns the `_CandidateDrawer` partial (model `ApplicationCard`) into
`#ats-drawer-host`; `site.js` wraps that body in the backdrop + sliding panel and closes it on
backdrop click, the close button (`data-drawer-close`), Escape, or the `ats:drawer-close` event.
`Applications/Details` renders the same `_CandidateDrawer` partial full-page as a deep-link / no-JS
fallback, so the two surfaces never drift. A page with its own bespoke header (the board) sets
`ViewData["HidePageHead"] = true` so the layout does not also emit the auto H1.

## Icon font (subset)
Material Symbols is self-hosted as a **subset**: `material-symbols-subset.woff2` (~216 KB), flattened
to the fixed axes the `.ms` class renders at and containing only the ~50 icons the app uses. The
`@font-face` for it lives in `ats-base.css`; the layouts no longer link LibMan's `outlined.css`.
**After adding a new icon, regenerate the subset:** `py tools/subset-material-symbols.py`. That script
scans every view + the icon-name literals in `SidebarNavViewComponent` and `DashboardService`,
intersects with the ligatures the full font defines, rewrites the subset + `tools/material-symbols.icons.txt`,
and fails if any used icon would be missing. LibMan still restores the full `material-symbols-outlined.woff2`
as the re-subset source (not served at runtime).
