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
  `Admin:`) from an in-code `NavItem[]`. A `NavItem` carries a `NavGroup`, an optional `RequiredPermission` (an `AtsPermission` constant),
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
`class="form-control"`, primary action `class="btn btn-primary"`.

**Validation (themed, no native bubbles) — enforced by `tests/e2e/form-validation.spec.ts`.**
There is no jQuery or jQuery Validation any more. Forms use native HTML5 constraints, and
`wwwroot/js/ats-validation.js` (rendered by `_ValidationScriptsPartial`, which all three layouts
include; the career site gets it without `site.js`) replaces the browser bubble:
- It cancels every `invalid` event (document, capture phase), writes a short British English message
  under the field, sets `aria-invalid="true"` and appends the message id to `aria-describedby`
  (existing ids are kept), focuses the first invalid field, and clears the message once the field is
  valid again (`input`/`change`). Invalid forms never submit, boosted ones included: the browser
  blocks the submit before htmx sees it.
- The message goes into the field's `asp-validation-for` span when the form has one, so a client
  and a server (ModelState) message never appear side by side; otherwise a span is created after
  the field. Server-rendered errors get the same aria wiring on load.
- `[Required]`, `[StringLength]` and `[RegularExpression]` reach the browser as `data-val-*` only;
  the script copies them onto `required` / `maxlength` / `minlength` / `pattern` on load, after every
  htmx swap and again on each submit click (so rows added by page scripts are covered).
  A `[RegularExpression]` pattern lands in an HTML `pattern` attribute, which browsers compile with
  the `v` flag, so it must be valid there: inside a character class escape `(` `)` `[` `]` `{` `}`
  `/` `-` `|`. An invalid pattern is silently ignored and the field is then checked only on the
  server. A `data-val-required` field that is not rendered (a removed pipeline stage row) is left unrequired,
  since the browser cannot focus it.
- Styling is token-mapped in `ats-tokens.css`: `.field-validation-error` (danger ink) and
  `.form-control[aria-invalid="true"]` (danger border). Do not use Bootstrap's `.is-invalid`: its
  icon is a hard-coded `#dc3545`.

**Add a validated field:** put the attribute on the view model (`[Required]`, `[EmailAddress]`,
`[StringLength]`), render `<input asp-for="X" class="form-control" />` plus
`<span asp-validation-for="X" class="text-danger small"></span>`. For an input without a model
property (e.g. the resume `type="file"`), write the native attribute (`required`, `type="email"`)
yourself; give it a label or `aria-label`. Never add `novalidate` or call `reportValidity()`.
Always validate on the server too: client constraints are a convenience.

## Add a new back-office page (checklist)
1. Controller action returns `View(...)`; the page is `Views/<Controller>/<Action>.cshtml`.
2. First line: `@{ ViewData["Title"] = "..."; }` (drives the H1 and browser title); optionally
   `ViewData["Eyebrow"] = "...:";` for the mono kicker.
3. Use Bootstrap grid + the `.ats-*` component classes. No inline theme colours; icons via `.ms`.
4. Header buttons: define a `@section PageActions { ... }`, or set the `TopBarAction*` `ViewData`
   keys for a topbar CTA.
5. To surface in the sidebar, append a `NavItem` in `SidebarNavViewComponent` with its `NavGroup`
   (and, if it should be permission-gated, `RequiredPermission`). Add a matching crumb in `TopBarViewComponent`.
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
  `site.js` disables a form's submit button once its request goes out (see Boosted navigation rule 4)
  and wires the `Ctrl/Cmd+K` search shortcut.

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

**Five rules that are easy to break:**
1. **`hx-target` / `hx-select` are inherited by every descendant.** Because they now sit on
   `#ats-content`, anything *inside* it that drives its own htmx request must override them or it
   will filter its own response for `#ats-content` and swap nothing. Today that is exactly one
   element — the board move button, which sets `hx-select="unset" hx-select-oob="unset"` on top of
   its own `hx-target`. The global search and top bar live *outside* `#ats-content` and are
   unaffected. **Any new htmx element inside the content area must do the same.**
   `<body>` still carries no boost config: putting it there would catch the top bar too.
2. **Shared libraries load in `<head>`; page scripts render inside `<main>`.** `@section Scripts` is
   rendered inside `#ats-content` so page JS re-runs after a swap — and `<main>` parses *before* the
   end of `<body>`, so anything a page's inline init needs (htmx, Sortable, validation) must
   already be defined. Add a new shared library to `<head>`, never per-page.
3. **Document title comes from `data-page-title` on `#ats-content`,** not from parsing the response:
   htmx replays cached DOM on Back/Forward with no HTTP response.
   **Anything outside `#ats-content` that varies per page must be listed in `hx-select-oob`.**
   Both boosted containers carry `hx-select-oob="#ats-sidebar,#ats-topbar"`. The top bar was missing
   from that list, so the breadcrumb silently kept showing the page you came from on every boosted
   navigation. If you add another per-page region outside the content area, add its id there too.
4. **Confirmation uses `hx-confirm`, never `onsubmit="return confirm(...)"`.** A boosted submit is
   driven by htmx, which does not consult the native `onsubmit` return value, so a native confirm
   silently stops gating the action. Every destructive form uses `hx-confirm`, which shows the
   themed confirm modal (see "Confirm dialog" below), never the browser's native dialog.
   `site.js` disables an htmx form's submit button on `htmx:beforeRequest` (which fires only after
   the confirm is accepted) and re-enables it when `htmx:afterRequest` reports failure. Plain forms
   are disabled on the next tick after `submit`, unless something called `preventDefault`. Never go
   back to disabling on the `submit` event itself: it runs before the confirm dialog, so cancelling
   left the button dead until reload.
   The drawer lives outside `#ats-content`, so a form in it (Remove from job) carries the boost
   config itself; `site.js` re-runs `htmx.process` after wrapping the drawer, and keeps a drawer that
   issued a boosted request open until the new page is swapped in. It then closes the drawer and moves
   focus to the new page's `h1` (or `#ats-content`, with `tabindex="-1"`), since the trigger that
   opened the drawer was swapped away and focus would otherwise fall to `<body>`.
5. **History snapshots cover `.ats-shell` only (`hx-history-elt`), enforced by
   `tests/e2e/history-restore.spec.ts`.** htmx snapshots its history element before each boosted
   swap and puts it back on Back/Forward. Without `hx-history-elt` that element is `<body>`: a restore
   re-ran `site.js` (every listener bound again, one confirm sent twice), duplicated `#ats-confirm`
   and brought back a `.modal-backdrop` that was mid-fade when the snapshot was taken, blocking the
   page until reload. Anything that is live state rather than page content (`site.js`,
   `#ats-confirm`, `#ats-drawer-host`, `#ats-toasts`, Bootstrap backdrops) must stay **outside**
   `.ats-shell`; page `@section Scripts` inside `#ats-content` still re-run on restore. `site.js`
   also closes a confirm left open when Back fires and removes any stray backdrop.

Opt out with `hx-boost="false"` for anything whose response is not a back-office page (file
downloads; the sign-out form, whose response is the login page on `_AuthLayout`). `site.js` also
falls back to a real navigation for any non-HTML response, any page lacking `#ats-content`, and any
non-2xx or network error — so a boosted click can never silently do nothing.

## Confirm dialog — enforced by `tests/e2e/confirm-modal.spec.ts`
One Bootstrap modal, `#ats-confirm` in `_Layout` (outside `#ats-content`, so a swap never removes
it), serves every `hx-confirm`. `site.js` handles `htmx:confirm`: only when `evt.detail.question` is
set (the element or an ancestor has `hx-confirm`) it prevents the native dialog, fills the modal and
calls `evt.detail.issueRequest(true)` from the confirm button alone. Cancel, Escape and a backdrop
click send nothing. One confirm at a time: a second one while the modal is open is dropped; one
that arrives while it is fading out is held and shown once it has closed. Focus
moves to Cancel for a danger confirm (to the confirm button otherwise), is trapped by Bootstrap, and
returns to the trigger on close (a hidden dropdown item hands it to its menu toggle).

```cshtml
<form asp-action="Delete" asp-route-id="@x.Id" method="post"
      hx-confirm="Delete this pipeline?"
      data-confirm-title="Delete pipeline" data-confirm-ok="Delete pipeline" data-confirm-variant="danger">
```
- `hx-confirm` is the message. `data-confirm-title` (default "Are you sure?"), `data-confirm-ok`
  (confirm button text, default "Confirm"; name the action, e.g. "Delete job", "Publish job") and
  `data-confirm-variant="danger"` (solid `.btn-danger`, for Delete/Remove; otherwise `.btn-primary`)
  are optional, but every existing form sets all that apply.
- It works in the candidate drawer: the modal (z-index 1055, backdrop 1050) sits above the drawer
  (1045), and the drawer's Escape/Tab handler ignores keys inside `.modal`, so Escape closes only the
  confirm.
- Modal styling is Bootstrap variables mapped to tokens in `ats-tokens.css` (`.modal`,
  `.modal-backdrop`, `.btn-danger`). Never add a second modal system.
- e2e: import `test`/`expect` from `tests/e2e/confirm.ts` in any spec that confirms; its `page`
  fixture fails the test if a native dialog fires. Use `acceptConfirm(page, message?)` /
  `dismissConfirm(page, message?, 'cancel' | 'escape' | 'backdrop')`, never `page.on('dialog')`.
  Pages from `browser.newPage()` fall outside the fixture; a native dialog there is auto-dismissed
  and the following `acceptConfirm` times out, so the regression still fails.

## Layout invariants (Phase 9) — enforced by `tests/e2e/layout-audit.spec.ts`
- **The content area has no width cap.** It shares the top bar's `1.75rem` horizontal padding, so
  a page's left edge lines up with the breadcrumb and the right gutter matches the left at any
  width. Two earlier attempts were wrong and are worth not repeating: a bare `max-width: 1400px`
  left-aligned everything (240px of dead space on the right at 1920px), and centring that cap then
  pushed the content 106px out of line with the breadcrumb. Locked by four assertions in
  `responsive.spec.ts` at 1280/1440/1920/2560px.
- **A card inside a grid column fills that column** (`height: 100%`). Without it, a card whose
  content wraps to an extra line ends lower than the card beside it and the pair reads as broken.
  Do not add `align-items-start` to a `.row` of parallel cards; it defeats this.
- **Any flex item holding wrapping text needs `min-width: 0`.** A flex item defaults to
  `min-width: auto` and refuses to shrink below its longest word. This is what made the audit log
  wrap one or two characters per line at 375px.
- **Tenant-authored strings are rendered verbatim.** Stage names are user-editable, so they are
  never lower-cased or humanised in a view; if a name reads badly, that is data for the tenant to
  fix on the Pipelines page. Truncate with ellipsis and a `title`, never by letting text run under
  a neighbour.
- **A grid that cannot collapse scrolls in its own container** (`.table-responsive`), never the page.

## Breadcrumbs (Phase 9)
`TopBarViewComponent` maps controller -> (group, page). On any action other than `Index` it also
emits a **link back to that section**, so a sub-page has a way out that is not the browser Back
button. A section index emits no link to itself.

## Tables (Phase 5, UX-1)
Column templates are CSS classes (`.ats-table--jobs`, `--candidates`, `--deliveries`, `--org`) applied
to both the `.ats-thead` and each `.ats-trow`. **Never set `grid-template-columns` inline** — an inline
style beats every media query, which is what made the tables impossible to make responsive. Under
768px the templates collapse to a single column and the header row is hidden. Adding a screen means
adding one class next to the others, not a `style=` attribute.

**Row menus.** A `.dropdown` inside an `.ats-trow--link` row must set
`data-bs-popper-config='{"strategy":"fixed"}'` on its toggle, or `.ats-card-flush`'s overflow clips
the menu on the first and last rows. Every row's controls share `z-index: 2`, so a later row would
paint over an open menu; `.ats-trow--link:has(.dropdown-menu.show)` lifts the open row above the rest.
Locked by `tests/e2e/row-menus.spec.ts`. A menu that needs more width takes a class
(`.ats-menu-wide`), never an inline `min-width`.
Menu styling is `--bs-dropdown-*` mapped to tokens on `.dropdown-menu` in `ats-tokens.css`; Bootstrap's
pressed item is a hard-coded `#0d6efd`, so never drop that mapping. A destructive item is
`.dropdown-item text-danger` (danger ink, danger-soft on hover/focus/active). Locked by
`tests/e2e/dropdown-theme.spec.ts` (no Bootstrap blue, axe colour-contrast on focus/hover/press).

**Icon-only row controls** (edit / delete glyphs on Organisation and Pipelines) take `.ats-icon-hit`
for a 24x24px minimum target (WCAG 2.5.8); a destructive glyph adds `.ats-icon-danger` for its colour.
Never colour one with an inline `style`.

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
`.ats-on-dark-*`). Raw hex belongs only where tokens are *defined*: `ats-tokens.css` (NowOnline palette and the
Bootstrap `-rgb` triples) and the Branding view component (per-tenant token values). A dark theme is now a token swap; it has not been built.

## Status colours and native controls — enforced by `tests/e2e/status-colours.spec.ts`
- `ats-tokens.css` maps Bootstrap's `--bs-{danger,success,warning,info}` (+ `-rgb`, `-text-emphasis`,
  `-bg-subtle`, `-border-subtle`) and `--bs-form-{valid,invalid}-*` to NowOnline tokens. The colour is
  the `--no-*-ink` shade (AA on white, the app background and its own soft tint), the subtle
  background is `--no-*-soft` (same pair as the pills), the border is `--no-*-border`. So
  `text-danger`, `text-success`/`-warning`/`-info`, `alert-*` and field errors are on-brand with no
  extra class. **The `-rgb` triples are literal numbers**: change a `--no-*-ink` and update its triple.
- For a status-coloured bit of text in a view, use `text-danger` etc., not an inline `color:`.
  `PillToneCss.Ink` returns those classes (neutral: `.ats-ink-muted`).
- The file input's "Choose file" button (`::file-selector-button`), checkbox focus glow and the colour
  picker (`.ats-color-input`) are themed too.
- Former inline colours are classes in `ats-components.css`: stage ramp fills `.ats-stage-1..5`
  (plus `-empty`, `-reached`, `-rejected`; index is `i % 5 + 1`), connection dots `.ats-dot--on/--off`,
  `.ats-divider-top/-bottom`, `.ats-icon-tile-dark`, `.ats-check-panel`, `.ats-on-dark-label`, and the
  back-office career hero preview `.ats-hero-preview*`. Data-driven sizes (bar `width`/`flex`) and the
  computed `_Avatar` colours stay inline.
- `.btn-danger` hover/active is `color-mix()` of the danger ink with Oxford Blue; there is no token
  for it, so it stays.

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
