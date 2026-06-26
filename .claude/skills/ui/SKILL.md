---
name: ui
description: The Ats back-office UI pattern - layouts, design tokens, shared components, and the exact steps to add a new page consistently. Read before building or changing any view.
---

# Ats UI

## Stack
Server-rendered ASP.NET Core MVC + Bootstrap 5 (self-hosted in `wwwroot/lib`). Icons: Bootstrap
Icons (`<i class="bi bi-..."></i>`). Client libraries are managed in `libman.json`; run
`libman restore` to hydrate `wwwroot/lib`. htmx + SortableJS are present for the Phase 1 kanban.
No external/CDN requests at runtime.

## Layouts
- `Views/Shared/_Layout.cshtml` - authenticated app shell: left sidebar (`<vc:sidebar-nav>`) plus a
  content area that renders `_Alerts`, `_PageHeader`, then the body. Default for all back-office pages.
- `Views/Shared/_AuthLayout.cshtml` - centered card for anonymous pages (login, register).
- Layout selection is declarative: `Views/_ViewStart.cshtml` sets `_Layout`;
  `Views/Account/_ViewStart.cshtml` overrides to `_AuthLayout`.

## Design tokens
All theming lives in `wwwroot/css/site.css` as CSS custom properties (`--ats-accent`, sidebar
colors, spacing, radius) plus a few Bootstrap overrides. Change the accent in one place. Do not put
theme colors inline in views.

## Shared components
- `SidebarNavViewComponent` (`ViewComponents/`) renders the nav from an in-code `NavItem[]`, marks
  the active item from the current controller, and shows the signed-in user (`Name` claim) and role.
  Add a new section by appending a `NavItem`.
- `_PageHeader.cshtml` renders the page title from `ViewData["Title"]`. Every content page sets
  `ViewData["Title"]`.
- `_Alerts.cshtml` renders `TempData["Success"|"Error"|"Info"]` as Bootstrap alerts. Set those in
  controllers for post-redirect feedback.

## Forms
Use tag helpers: `asp-for`, `asp-validation-for`, `asp-validation-summary="ModelOnly"`. Inputs get
`class="form-control"`, primary action `class="btn btn-primary"`. Client-side validation is wired in
`_AuthLayout`; for back-office forms add `<partial name="_ValidationScriptsPartial" />` in a
`@section Scripts`.

## Add a new back-office page (checklist)
1. Controller action returns `View(...)`; the page is `Views/<Controller>/<Action>.cshtml`.
2. First line: `@{ ViewData["Title"] = "..."; }` (drives the header and browser title).
3. Use Bootstrap layout (`row`, `col-*`, `card`) for content. No inline theme colors.
4. To surface in the sidebar, append a `NavItem` in `SidebarNavViewComponent`.
5. For flash messages, set `TempData["Success"]` etc. in the action.

## Security
Antiforgery is validated globally (`AutoValidateAntiforgeryToken`); form tag helpers emit the token.
The auth cookie is `HttpOnly` + `Secure`, so test over https.
