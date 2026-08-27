---
name: audit
description: The Ats audit log, dashboard metrics, and integration test tools - what is recorded, how, and where shown. Read before changing auditing or dashboard behavior.
---

# Ats Audit and Dashboards (Phase 4)

## Audit log
`AuditEntry` (TenantEntity): Action, EntityType, EntityRef, Summary, UserId, UserName, OccurredAt.
`IAuditLogger.LogAsync` writes one entry, stamping the user from `ICurrentUser` (the `Name` claim), and
never throws into the caller. Back-office controllers call it after successful mutations: job
create/publish/close/delete, pipeline save/delete, department/location create/update/delete, candidate
create/update, integration settings save, feed-key regenerate. Stage moves are not duplicated here; they
live in `ApplicationEvent` (application Details) and outbound deliveries in `WebhookDelivery` (Integration
delivery log). The Owner-only `AuditController` shows recent entries via `IAuditQuery`.

## Dashboard
`IDashboardService.GetAsync` returns a `DashboardSummary` (published jobs, candidates, active
applications, active-by-stage counts, recent applications), all tenant-scoped. `DashboardController`
renders it.

## Integration test tools
The Integration page shows the count of jobs the feed would return and a Test-connection button that
calls `IReferralToolClient.CheckVacancyExistsAsync` with the saved settings and a sample published
`ExternalRef`. It is a read-only probe: it never sends a status update or writes a `WebhookDelivery`.
The ReferralTool HttpClient is registered in `AddAtsInfrastructure` (shared by Web and Worker).

## Dashboard use of the audit log (redesign)
`DashboardService` projects the most recent `AuditEntry` rows into the dashboard's "What moved."
activity feed, and derives the "Needs you" list from live counts (idle applications, failed outbox
deliveries, draft jobs) rather than any notification store. Time-to-hire, offer acceptance and the
source split are computed there too via `DashboardMath` (unit-tested in `Ats.Tests`). Offer
acceptance currently uses a documented proxy denominator (applications that progressed in the last
90 days); see the comment in `DashboardService`.

## Audit screen (redesign)
`IAuditQuery.SearchAsync` (filtered by free text / action / date-from, paged) backs the rebuilt audit
screen; `RecentAsync` still backs the dashboard feed. `DistinctActionsAsync` populates the action
filter. Gotcha: the controller's action-filter parameter binds from the query key `act`, NOT
`action` — `action` is a reserved MVC route token and would bind to the current action name, silently
filtering out every row. Keep it named `act`.

## Dashboard correctness notes (Phase 3/4)
- **Offer acceptance** counts **distinct applications** that reached a hired-outcome stage, via
  `DashboardMath.OfferAcceptancePercent`. Counting hire *events* over-counted a re-entry into a hired
  stage. Time-to-hire likewise uses each application's **first** arrival at a hired stage.
- Both metrics need at least one stage flagged `IsTerminal` with `TerminalOutcome = Hired`. A pipeline
  whose stages are merely *named* "Hired"/"Rejected" reports no hires and leaves applications `Active`.
- Outbox tiles come from **one** `GroupBy(Status)` query, not a COUNT per status.
- `OutboxStatus.Processing` (a worker has claimed the message) is transient and counts as **Pending**
  for display, in both the tiles and the delivery-log filter. Treating it as its own bucket makes
  in-flight messages vanish from the UI.
- Activity rows carry `OccurredAt` as a UTC instant and render through `<local-time>`; do not
  pre-format times in the read model.
