# Design Spec: ATS Phase 4 - Polish

- Date: 2026-06-29
- Status: Proposed (awaiting review)
- Author: Henil Patel (with Claude)
- Related: `2026-06-26-ats-product-design.md` (Sections 11, 12 Phase 4), Phases 0-3 entities/services,
  `.claude/skills/ui/SKILL.md`.

---

## 1. Purpose and scope

Bring the back-office to product-grade usability after the core product (Phases 0-3) is feature-complete.

### In scope
- **Audit views:** an action log of back-office mutations + Owner-visible view; link out to the existing
  `ApplicationEvent` history and `WebhookDelivery` log.
- **Dashboards:** real tenant-scoped metrics on the dashboard.
- **Test-feed / connection:** a feed preview and a ReferralTool connection test on the Integration page.
- **List pagination + search/filter:** Jobs, Candidates, and the delivery log.
- **Custom error pages:** styled 404 / 403 / 500 for the back-office and a careers-area 404.
- **UI/UX polish pass:** a bounded checklist of finishing touches on the existing UI baseline.

### Out of scope (explicitly deferred)
- **Email notifications** (excluded by decision this phase).
- Role-based access (RBAC) enforcement across screens and user management (not selected this phase;
  Owner-only gating on Integration remains as-is).
- Any redesign of the UI; the polish pass refines the existing baseline only.

### Structure: one spec, two plans
- **Plan A (data/feature polish):** audit log + views, dashboards, Test-feed/connection.
- **Plan B (cross-cutting UX polish):** pagination + search/filter, custom error pages, UI/UX pass.

---

## 2. Locked decisions

| Decision | Choice |
|---|---|
| Email notifications | Excluded this phase |
| Audit capture | Explicit `IAuditLogger` calls at back-office write points; config/admin actions only (stage moves stay in `ApplicationEvent`) |
| Audit visibility | Owner-only view |
| Dashboard metrics | Published jobs, total candidates, active applications, per-stage active-application counts, recent applications |
| Test connection | Calls ReferralTool `checkvacancyexists` with saved credentials and a sample published `ExternalRef`; reports reachable/HTTP result |
| Feed preview | Count of jobs the feed would return for the tenant |
| Pagination | Shared `PagedResult<T>` + `page`/`pageSize`; applied to Jobs, Candidates, delivery log |
| Error pages | `UseStatusCodePagesWithReExecute` + `UseExceptionHandler` for back-office; careers 404 view |
| Schema | One new `AuditEntry` table (Plan A migration); nothing else |

---

## 3. Plan A: data/feature polish

### 3.1 Audit log
- `AuditEntry : TenantEntity` - `Action` (e.g. "JobPublished"), `EntityType` (e.g. "Job"), `EntityRef`
  (a human id such as `JOB-1` or a key), `Summary` (short text), `UserId?`, `UserName`, `OccurredAt`.
- `IAuditLogger.LogAsync(action, entityType, entityRef, summary, ct)` (Application abstraction; impl in
  Infrastructure writes an `AuditEntry`, stamping `UserId`/`UserName` from `ICurrentUser`). It records
  but never throws into the caller's flow (failures are swallowed and logged).
- Called from the existing services/controllers at: job create / publish / close / delete; pipeline
  template save / delete; department + location create / update / delete; candidate create / update;
  integration settings save; feed-key regenerate. Stage moves are not duplicated here (they live in
  `ApplicationEvent`, shown on the application Details page).
- `AuditController` (Owner-only) lists entries newest-first, paginated (Plan B's pager), with the
  existing per-application history and delivery log reachable from their current pages.
- Migration `AddAuditEntry` (created by AI, applied by developer).

### 3.2 Dashboards
- `IDashboardService.GetAsync()` returns a `DashboardSummary` (PublishedJobs, TotalCandidates,
  ActiveApplications, a list of (stage name, count) for active applications, and a small list of recent
  applications with candidate name + job title + applied time). All tenant-scoped via the global filter.
- `DashboardController.Index` is wired to the summary; the placeholder cards become real numbers and a
  per-stage table plus a recent-applications list.

### 3.3 Test-feed / connection (Integration page)
- **Feed preview:** a `GET`/action that reports the count of non-draft jobs the feed would return for the
  tenant (reusing `IVacancyFeedRepository`), shown inline.
- **Test connection:** a POST that builds `ReferralToolSettings` from the saved `TenantSettings`, picks a
  sample published job's `ExternalRef` (or reports "no published job to test with"), calls
  `IReferralToolClient.CheckVacancyExistsAsync`, and shows reachable / HTTP status / exists result. Never
  sends a status update; read-only probe.

---

## 4. Plan B: cross-cutting UX polish

### 4.1 Pagination + search/filter
- `PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)` in
  `Ats.Application`. Repositories gain paged/filtered query methods; services accept a small query
  input (page, pageSize, optional search/status).
- **Jobs:** filter by `JobStatus` (optional) + search on Title/ExternalRef. **Candidates:** search on
  name/email. **Delivery log:** filter by `OutboxStatus`.
- A reusable `_Pager.cshtml` partial (prev/next + page indicator) and a search box on each list. Default
  page size 20.

### 4.2 Custom error pages
- Back-office: keep `UseExceptionHandler("/Home/Error")` (styled 500), add
  `app.UseStatusCodePagesWithReExecute("/Home/Status/{0}")` with a `HomeController.Status(code)` that
  renders friendly 404 and 403 views inside the app shell.
- Careers area: a styled 404 view shown when a slug/job is not found, using the `_CareersLayout`.

### 4.3 UI/UX polish pass (bounded checklist)
- A favicon and consistent "ATS" wordmark across both layouts.
- Submit buttons show a busy/disabled state on form submit (small unobtrusive script).
- Consistent empty states and "New X" button placement across all list pages.
- Dismissible flash alerts (Bootstrap close button on `_Alerts`).
- Tidy spacing/table styling consistency; a focus/contrast check against the design tokens.
- No new screens or redesign; refinements only. Documented against `.claude/skills/ui/SKILL.md`.

---

## 5. Security and conventions

- Audit and dashboard data are tenant-scoped via the global filter; the audit view is Owner-only.
- Test connection is a read-only probe; it never emits a status update and does not log to
  `WebhookDelivery` (that table is for real outbox deliveries).
- No secrets are shown in audit summaries (e.g. feed-key regenerate logs the action, not the key).
- Restrictions unchanged: the AI does not commit, apply the `AddAuditEntry` migration, or deploy.
- Em dashes and emoji avoided in generated content.

---

## 6. Verification

Build + run. Manual checks:
- Audit: perform several back-office actions (publish a job, edit a pipeline, save integration settings),
  then confirm they appear in the audit view with user + timestamp; confirm a non-Owner cannot reach it.
- Dashboard: confirm counts match the data (create/publish jobs, add candidates/applications) and the
  per-stage breakdown and recent list render.
- Test-feed/connection: feed preview count matches published jobs; Test connection reports a result
  (reachable/HTTP) without creating any application or delivery row.
- Pagination/search: lists page correctly and search/status filters narrow results; the pager works at
  boundaries.
- Error pages: a bad back-office URL shows the styled 404; a non-Owner hitting an Owner page shows 403;
  an unknown careers slug/job shows the careers 404.
- UI/UX: favicon present, submit busy-state works, empty states and alerts render consistently.
- Tenancy: all new views show only the current tenant's data.

---

## 7. Documentation maintenance (spec Section 16)

After Phase 4 lands: add `.claude/skills/audit/SKILL.md` (audit log + dashboard + the action capture
points); refresh `.claude/skills/ui/SKILL.md` with the pager partial, error pages, and the polish
conventions; update the `CLAUDE.md` skill-index; keep `docs/specs` and `docs/plans` current.

---

## 8. Notes

- One migration only (`AddAuditEntry`, Plan A). All other work is queries and UI.
- Phases 0-3 remain the complete end-to-end product; Phase 4 is usability polish on top.
