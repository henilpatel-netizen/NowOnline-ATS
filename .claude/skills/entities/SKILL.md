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
  points at a stage.
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
