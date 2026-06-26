# Design Spec: ATS Back-Office UI Baseline

- Date: 2026-06-26
- Status: Proposed (awaiting review)
- Author: Henil Patel (with Claude)
- Related: `2026-06-26-ats-product-design.md` (Section 4 solution structure, Section 12 phases)

---

## 1. Purpose and scope

Establish a styled, consistent, best-practice MVC user interface foundation for the ATS
back-office before Phase 1 builds feature screens. The goal is a single UI pattern and design
language that every later phase (jobs, pipelines, candidates, career site) reuses, so styling is
defined once and applied everywhere instead of being retrofitted in Phase 4.

This is foundation work plus restyling the three screens that already exist (Login, Register,
Dashboard). It is not feature work. No new entities, services, or database changes.

### In scope
Shared layouts, a reusable navigation component, shared partials, a design-token layer over
Bootstrap 5, a self-hosted icon set, client-library management via LibMan, restyling the existing
three screens, routing cleanup, and a documented UI skill so the pattern is repeatable.

### Out of scope (deferred)
- Wiring htmx + SortableJS into a kanban board (Phase 1; the libraries are registered now but unused).
- A real product name, logo, and brand identity (decided later; the baseline uses a neutral "ATS"
  wordmark and one accent color that is trivial to rebrand).
- Deep responsive refinement and dark mode beyond what Bootstrap provides by default (Phase 4 polish).
- Public career-site visual design (Phase 2; it reuses this token layer with its own layout).

---

## 2. Locked decisions

| Decision | Choice |
|---|---|
| Back-office navigation | Left sidebar app shell (scales as sections grow; full width for the kanban) |
| Branding | Neutral placeholder: "ATS" wordmark, single accent color, no logo |
| CSS base | Bootstrap 5 (already bundled in `Ats.Web/wwwroot/lib`) |
| Icons | Bootstrap Icons, self-hosted |
| Client-library management | LibMan (`libman.json`), so client deps are declared and restorable |
| Kanban interactivity (Phase 1) | htmx + SortableJS (server-rendered partials POST stage moves) |
| External requests | None. All client libraries are self-hosted (CSP-friendly, honors the no-external-data rule) |

---

## 3. Layouts

Two layouts, selected per area:

- `Views/Shared/_Layout.cshtml` (authenticated app shell): a left sidebar (~220px) containing the
  "ATS" wordmark, the navigation, and a footer block with the current user (`DisplayName` and role)
  plus a Sign out button. To the right is the main content region with a page-title header slot.
- `Views/Shared/_AuthLayout.cshtml` (anonymous): a centered card (~400px) on a plain background for
  Login and Register, with the "ATS" wordmark above the card and no sidebar.

`Views/_ViewStart.cshtml` defaults to `_Layout`. A nested `Views/Account/_ViewStart.cshtml` overrides
Login and Register to `_AuthLayout`. This keeps layout selection declarative rather than per-view.

---

## 4. Shared components (consistency mechanism)

- `SidebarNavViewComponent` (`ViewComponents/SidebarNavViewComponent.cs` + a default view): renders
  the sidebar navigation from a small in-code list of nav entries, marks the active item from the
  current route, and shows the current user from claims. Phase 1+ adds entries (Jobs, Pipelines,
  Candidates, Settings) by appending to the list. Phase 0 shows only Dashboard, so there are no dead
  links to unbuilt screens.
- `Views/Shared/_PageHeader.cshtml`: a partial rendering a page title and an optional actions slot,
  used at the top of every content page for a consistent header.
- `Views/Shared/_Alerts.cshtml`: a partial that renders `TempData` flash messages (success, error,
  info) as Bootstrap alerts, included once in `_Layout`.
- Validation is rendered with the standard tag helpers (`asp-validation-summary`, `asp-validation-for`)
  styled by Bootstrap; client-side validation uses the already-bundled jQuery-validation via
  `_ValidationScriptsPartial`.

---

## 5. Design tokens

A small `wwwroot/css/site.css` layer defines CSS custom properties (a single accent color, surface
and border neutrals, spacing rhythm) and a thin set of component overrides on top of Bootstrap. All
color and accent usage references these tokens, so the look is centralized and rebrandable from one
file. No per-view inline styling for theming.

---

## 6. Screen restyles

- Login and Register: Bootstrap form controls (`form-control`, labels, `btn btn-primary`), the styled
  validation summary, and client-side validation enabled. Rendered in `_AuthLayout` as a centered card.
- Dashboard: rendered in `_Layout` (sidebar shell) with a `_PageHeader` and a row of placeholder
  metric cards (Open jobs, Candidates, In pipeline) showing a neutral placeholder until Phase 1
  supplies data. Sign out lives in the sidebar footer, not on the page body.

---

## 7. Routing cleanup

- Root `/` redirects to `/Dashboard`, which already redirects unauthenticated users to
  `/Account/Login`. The product no longer opens on the MVC template Welcome page.
- The template Home and Privacy scaffolding is removed from navigation so the app reads as a product
  rather than a template. (The `HomeController` is repurposed to the redirect; unused template views
  are removed.)

---

## 8. MVC best practices applied

- View Components for reusable, data-aware UI (the sidebar) rather than copy-pasted markup.
- Partials for repeated fragments (page header, alerts).
- Declarative layout selection via `_ViewStart` and a nested `_ViewStart`.
- Tag helpers for forms, links, and validation.
- LibMan for client-library acquisition and restore.
- Static asset delivery via the template's existing `MapStaticAssets` (fingerprinted assets).
- Accessibility: labelled inputs, `aria-label` on icon-only controls, sufficient contrast from tokens.
- No inline event handlers and no external requests, keeping a tight content security posture.

---

## 9. Project-wide consistency and documentation

- The token layer, Bootstrap base, shared partials, and View Component are the single source of truth
  for look and structure. Phase 2's career site reuses the tokens with its own public layout; Api
  error responses reuse the tokens if they render HTML.
- A new `.claude/skills/ui/SKILL.md` documents: the two layouts and when each applies, the design
  tokens and how to change the accent, icon usage, the form/button/validation pattern, the alert and
  page-header partials, and the exact steps to add a new back-office page that matches the pattern.
- `CLAUDE.md` skill-index gains a "UI" row pointing at the new skill; the architecture skill references
  it for "where UI code goes."

---

## 10. Verification

- `dotnet build` succeeds with no new warnings.
- App runs: `/Account/Login` and `/Account/Register` render as the centered auth card with working
  client-side validation; the Dashboard renders inside the sidebar shell with the metric cards; the
  sidebar shows the signed-in user and Sign out works; `/` redirects to the Dashboard (and to Login
  when signed out).
- The `.claude/skills/ui/SKILL.md` exists and matches what was built; the `CLAUDE.md` skill-index
  lists it.

---

## 11. Notes

- Restrictions unchanged: the AI does not commit, apply migrations, or deploy. This baseline touches
  no database, so there is no migration.
- Em dashes and emoji are avoided in all generated content per the working conventions.
