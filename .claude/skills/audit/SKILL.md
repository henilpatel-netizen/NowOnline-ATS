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
