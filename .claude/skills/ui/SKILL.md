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

## Timestamps (Phase 3, DATA-4)
Never render a stored time directly and never call `ToLocalTime()` — that would show the **server's**
timezone. Store UTC; display via the tag helper:

```cshtml
<local-time utc="@Model.OccurredAt" format="short"></local-time>
```

- Formats: `date` | `datetime` | `short` | `monthday` | `time` | `weekday`. `empty="—"` covers nulls.
- **Always write the explicit `></local-time>` end tag.** A self-closing `<local-time ... />` makes
  Razor swallow the markup that follows it (this silently dropped trailing text in the delivery log and
  the dashboard eyebrow). `LocalTimeTagHelper` forces `TagMode.StartTagAndEndTag` as a backstop.
- The helper emits `<time datetime="...UTC..." data-local="format">` with a UTC-labelled fallback;
  `site.js` rewrites the text to the viewer's zone and re-runs on `htmx:afterSwap`.
- Only the **zone** follows the viewer. The format is assembled from parts explicitly so the house
  day-first, 24-hour style (`30/06 15:22`) is identical on every machine.
- For "today" in the page header, set `ViewData["EyebrowUtc"] = DateTimeOffset.UtcNow` (the layout
  renders it per viewer) rather than formatting a date string.

## Static assets (Phase 4, PERF-6)
`MapStaticAssets` fingerprints everything under `wwwroot` at build time and serves the hashed route as
`Cache-Control: max-age=31536000, immutable`. **Do not add `asp-append-version`** — it suppresses the
fingerprinted-URL substitution and the asset falls back to a revalidated `no-cache` route. Any new
endpoint group needs `.WithStaticAssets()` (both `MapControllerRoute` and `MapControllers` have it) or
its views will emit unfingerprinted URLs.

## Boosted navigation (Phase 5 NAV-1, extended NAV-2) — read before touching the layout or adding htmx
Back-office navigation is AJAX: only `#ats-content` is swapped, so the shell, CSS, fonts and the
shared libraries are never re-fetched or re-executed.

Two containers carry the boost config, with identical attributes:
- `<nav class="ats-nav">` in `Components/SidebarNav` — the sidebar links.
- `<main id="ats-content">` in `_Layout` — every in-content link, pager, filter tab and form POST.

Measured with `tests/e2e/nav-cost.spec.ts`: an in-content navigation went from **1 document +
17 assets + 862ms** to **1 xhr + 0 assets + 42ms**. Keep that spec passing.

**Four rules that are easy to break:**
1. **`hx-target` / `hx-select` are inherited by every descendant.** Because they now sit on
   `#ats-content`, anything *inside* it that drives its own htmx request must override them or it
   will filter its own response for `#ats-content` and swap nothing. Today that is exactly one
   element — the board move button, which sets `hx-select="unset" hx-select-oob="unset"` on top of
   its own `hx-target`. The global search and top bar live *outside* `#ats-content` and are
   unaffected. **Any new htmx element inside the content area must do the same.**
   `<body>` still carries no boost config: putting it there would catch the top bar too.
2. **Shared libraries load in `<head>`; page scripts render inside `<main>`.** `@section Scripts` is
   rendered inside `#ats-content` so page JS re-runs after a swap — and `<main>` parses *before* the
   end of `<body>`, so anything a page's inline init needs (htmx, jQuery, Sortable, validation) must
   already be defined. Add a new shared library to `<head>`, never per-page.
3. **Document title comes from `data-page-title` on `#ats-content`,** not from parsing the response:
   htmx replays cached DOM on Back/Forward with no HTTP response.
4. **Confirmation uses `hx-confirm`, never `onsubmit="return confirm(...)"`.** A boosted submit is
   driven by htmx, which does not consult the native `onsubmit` return value, so a native confirm
   silently stops gating the action. All five destructive forms use `hx-confirm`.

Opt out with `hx-boost="false"` for anything whose response is not a back-office page (file
downloads; the sign-out form, whose response is the login page on `_AuthLayout`). `site.js` also
falls back to a real navigation for any non-HTML response, any page lacking `#ats-content`, and any
non-2xx or network error — so a boosted click can never silently do nothing.

## Tables (Phase 5, UX-1)
Column templates are CSS classes (`.ats-table--jobs`, `--candidates`, `--deliveries`, `--org`) applied
to both the `.ats-thead` and each `.ats-trow`. **Never set `grid-template-columns` inline** — an inline
style beats every media query, which is what made the tables impossible to make responsive. Under
768px the templates collapse to a single column and the header row is hidden. Adding a screen means
adding one class next to the others, not a `style=` attribute.

## Async feedback (Phase 5, UX-2/UX-3)
htmx toggles `.htmx-request` on whatever `hx-indicator` points at, so progress needs no JS:
- `.ats-spinner` — inline spinner (global search).
- `.ats-nav-progress` — top progress bar for boosted navigation.
- `.ats-board-card.htmx-request` — dims the card being moved; `hx-disabled-elt="find select"` blocks
  the double-submits that used to cause duplicate moves.
- `Ats.showDrawerSkeleton()` — paints the drawer skeleton synchronously on click.
- `Ats.toast(message, tone)` — transient message. Board move failures raise one and then re-fetch the
  board, so the UI can never silently disagree with the server.

## Colour (Phase 5, UX-5)
No one-off hex in views. Text on dark surfaces uses `--ats-on-dark`, `--ats-on-dark-muted`,
`--ats-on-dark-subtle`, `--ats-on-dark-label`, `--ats-on-dark-danger` (helper classes
`.ats-on-dark-*`). The only file that may contain raw hex is the Branding view component, which
*defines* the per-tenant token values. A dark theme is now a token swap; it has not been built.

## Accessibility (Phase 6, verified by axe in Phase 9) — the rules that are easy to undo
Target is WCAG 2.1 AA, enforced by `tests/e2e/a11y.spec.ts` (axe-core over 11 back-office screens
plus the public career site). Phase 6 shipped believing it was clean; the first axe run failed
**11 of 11 screens**. Do not trust a manual colour check.

**Text colour tokens are contrast-derived. Never use a raw brand colour for text:**
- `--ats-ink-subtle` / `--ats-ink-faint` are AA against every app surface (`#FFFFFF`, `#FAFBFC`,
  `#F5F6F7`). The brand palette's `--no-roman-silver` (`#88909A`) is only 2.98:1 on a subtle
  surface, so it is **not** a text colour.
- `--ats-accent-text` is the accent as *text* (links, `.btn-link`, the career-site CTA). The raw
  accent is a fill colour: the default `#0085CA` is only 4.03:1. `BrandColor.AccentText` darkens the
  tenant accent until it reaches 4.5:1, preserving hue rather than falling back to navy.
- Per-tenant sidebar tokens are emitted by `Components/Branding`; the light-theme sidebar label was
  3.23:1 until Phase 9.
- Every form control needs a real label. A `<th>` column header is **not** one — the pipeline stage
  grid needs an explicit `aria-label` per input.

- **Never navigate a row with `onclick="location.href"`.** (Row links are real links and, since
  NAV-2, are boosted along with everything else inside `#ats-content`.)
  A row's primary cell is a real `<a>` with
  `.ats-row-link`; its `::after` overlay stretches the hit area across the row, so the mouse behaves as
  before while the keyboard gets a genuine link. Anything else interactive in the row needs
  `position: relative; z-index: 2` (that is what `.ats-row-actions` is for). The focus ring is drawn on
  the row via `:focus-within`.
- **Colour on the accent is derived, never assumed.** `--ats-on-accent` (button/chip text) and
  `--ats-focus-ring` come from `BrandColor.OnAccent` / `BrandColor.FocusRing` via the Branding
  component. Do not hard-code `#fff` on anything sitting on `--ats-accent`: a pale tenant accent then
  becomes unreadable. `#fff` on a *fixed* dark surface (Oxford Blue panels) is fine.
- **Decorative icons are hidden automatically.** `MaterialIconTagHelper` adds `aria-hidden="true"` to
  every `span.ms`, because Material Symbols render as a text ligature and a screen reader would read
  the icon's name ("Submit application arrow_forward"). An icon that carries standalone meaning opts
  out by setting `role` or `aria-label`.
- **The drawer is a real modal.** `site.js` moves focus in on open, traps Tab, restores focus to the
  trigger on close, and labels itself via `aria-labelledby="ats-drawer-title"`. A new way of opening it
  must call `Ats.rememberDrawerTrigger(el)` first so focus can be returned. Openers must be focusable
  elements (a `<button>`), not a click handler on a div.
- Every input needs a label or `aria-label` — a `placeholder` is not an accessible name and disappears
  on input. Validation summaries carry `role="alert"` so errors are announced.
- Do not re-add `role="listbox"` to the search results panel: it holds plain links with no option
  semantics or arrow-key navigation. It is a labelled `role="region"` with `aria-live="polite"`.
