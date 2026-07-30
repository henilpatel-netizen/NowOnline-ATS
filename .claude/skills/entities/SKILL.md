---
name: entities
description: The Ats Phase 1 aggregates - Job, Candidate, JobApplication, ApplicationEvent - their rules, soft delete, ExternalRef, and where the data-access lives. Read before touching recruiting data.
---

# Ats Entities (Phase 1)

## Aggregates
- `Job` (TenantEntity, ISoftDeletable): Draft/Published/Closed lifecycle; `ExternalRef` = `JOB-{n}` from
  `TenantSettings.LastJobNumber` (stable, never reused). References Department/Location/PipelineTemplate.
- `Candidate` (TenantEntity, ISoftDeletable): deduped per tenant by `Email` (unique `(TenantId, Email)`).
- `JobApplication` (TenantEntity, ISoftDeletable): the candidate-on-a-job aggregate. Named
  `JobApplication` (not `Application`) to avoid colliding with the `Ats.Application` namespace; the
  DbSet is `Applications` and the table is `Applications`. One per `(TenantId, JobId, CandidateId)`;
  `RowVersion` optimistic concurrency; `Status` Active/Hired/Rejected/Withdrawn; `CurrentStageId`
  points at a stage. `Origin` (`ApplicationOrigin`: Unknown/CareerSite/Manual/Referral) is
  presentation-only — it drives source chips in the UI and is never read by the outbox, worker,
  feed, or ReferralTool client. Rows predating the column are `Unknown`, rendered as "Not recorded".
- `ApplicationEvent` (TenantEntity): append-only stage-move history (`FromStageId?`, `ToStageId`,
  `OccurredAt`, `MovedByUserId?`).

## Soft delete
`ISoftDeletable.IsDeleted` plus the global filter (`AtsDbContext`) hides deleted rows. Services set
`IsDeleted = true`; they never hard-delete Job/Candidate/JobApplication. Departments/Locations are hard
delete, guarded against in-use.

## Data access
One service + repository interface per aggregate in `Ats.Application/<Area>`, implemented in
`Ats.Infrastructure/Persistence/Repositories`. Services return `OperationResult` (defined in
`Ats.Application/Departments/DepartmentService.cs`). Stage moves use
`IApplicationRepository.SetExpectedRowVersion` + `TrySaveChangesAsync` for concurrency; the
`DbUpdateConcurrencyException` is caught in the repository so the Application layer stays EF-free.

## Read-model projections (redesign)
Screen read models live beside the aggregate they serve, contract in `Ats.Application/<Area>`,
EF projection in `Ats.Infrastructure/<Area>` (not the repositories folder): `IJobListQuery`
(jobs list: per-stage counts + applicant avatars), `ICandidateListQuery` (latest origin/job/stage
+ last activity), `IApplicationCardQuery` (drawer/detail: days-in-stage, referral code, delivery
state, resume size via `IFileStore.StatAsync`, stage progress, history). These are read-only and do
not go through the aggregate repositories. `Origin` (`ApplicationOrigin`) is stamped at creation
(career apply -> Referral/CareerSite, board/candidates add -> Manual); rows predating it are
`Unknown`, shown as "Not recorded".
