---
name: entities
description: The Ats Phase 1 aggregates - Job, Candidate, JobApplication, ApplicationEvent - their rules, soft delete, ExternalRef, and where the data-access lives. Read before touching recruiting data.
---

# Ats Entities (Phase 1)

## Aggregates
- `Job` (TenantEntity, ISoftDeletable): Draft/Published/Closed lifecycle; `ExternalRef` = `JOB-{n}` from
  `TenantSettings.LastJobNumber` (stable, never reused; it is the ReferralTool vacancy `Id` in the
  vacancy push). References Department/Location/PipelineTemplate.
- `JobHiringManager` (TenantEntity): the hiring team link between a job and a user. `Job.HiringManagers` is the
  collection. Unique `(TenantId, JobId, UserId)` and an index `(TenantId, UserId)` for scope lookups. FK to `Jobs`
  cascades; FK to `Users` is `Restrict` (users are deactivated, never deleted). Migration `AddJobHiringManagers`.
  A user is assignable only while active with role HiringManager. It drives job scoping (authorization skill).
- `Candidate` (TenantEntity, ISoftDeletable): deduped per tenant by `Email` (unique `(TenantId, Email)`).
- `JobApplication` (TenantEntity, ISoftDeletable): the candidate-on-a-job aggregate. Named
  `JobApplication` (not `Application`) to avoid colliding with the `Ats.Application` namespace; the
  DbSet is `Applications` and the table is `Applications`. One per `(TenantId, JobId, CandidateId)`;
  `RowVersion` optimistic concurrency; `Status` Active/Hired/Rejected/Withdrawn; `CurrentStageId`
  points at a stage. `Origin` (`ApplicationOrigin`: Unknown/CareerSite/Manual/Referral) is
  presentation-only — it drives source chips in the UI and is never read by the outbox, worker,
  vacancy push, or ReferralTool client. Rows predating the column are `Unknown`, rendered as "Not recorded".
- `ApplicationEvent` (TenantEntity): append-only stage-move history (`FromStageId?`, `ToStageId`,
  `OccurredAt`, `MovedByUserId?`).

## Soft delete
`ISoftDeletable.IsDeleted` plus the global filter (`AtsDbContext`) hides deleted rows. Services set
`IsDeleted = true`; they never hard-delete Job/Candidate/JobApplication. Departments/Locations are hard
delete, guarded against use by a live job; `Job.DepartmentId` / `LocationId` are `ON DELETE SET NULL`,
so a soft-deleted job (hidden from that guard) loses the reference instead of blocking the delete.

Deleting a candidate (`CandidateService.DeleteAsync`) soft-deletes the candidate and all their
applications in one `SaveChanges`, so they drop out of the list, search, board and dashboard counts.
The unique indexes on `(TenantId, Email)` and `(TenantId, JobId, CandidateId)` are filtered on
`IsDeleted = 0`, so the same email can apply again. ReferralTool is not told about a deleted
candidate (its API has no such call); outbox messages already queued for their applications still send.
A re-created candidate gets a new `Candidate.Key`, so ReferralTool sees a new candidate (known limitation).

Removing a candidate from one job (`ApplicationService.RemoveAsync`, drawer "Remove from job",
`ApplicationsController.Remove`, audit `ApplicationRemoved`) soft-deletes that application only; the
candidate and their other applications stay. It leaves the board and the job's counts, and the filtered
index lets the same candidate be added to that job again. As with candidate delete, queued outbox
messages for it still send and ReferralTool is not told. Re-adding does **not** resend a status that ReferralTool
has or may still get for the same code, vacancy and candidate, because its duplicate guard rejects an
identical stage; a `Failed` one it never received is sent again (rule and limits in the integration skill).

Both deletes save through `TrySaveChangesAsync`: applications carry a `RowVersion`, so a board move
between load and save returns "changed by someone else, reload and try again" instead of a 500.

Friendly delete failures (the database's foreign keys see rows the query filter hides):
- Pipeline delete: "still used by deleted jobs" (`IPipelineTemplateRepository.TryRemoveAsync`).
- Stage delete on save: "This stage still has candidates (including removed ones) and cannot be
  deleted." `Applications.CurrentStageId` is `Restrict`, and `TrySaveChangesAsync` maps FK error 547 to
  `TemplateSaveOutcome.StageInUse` and clears the change tracker.

Dashboard counts: hire events join the filtered `Applications`, so removed or deleted applications drop
out of time-to-hire and the offer-acceptance numerator. The acceptance denominator counts raw stage
events (`ApplicationEvent` is not soft-deletable), so their moves still count there.

Migration `FilterUniqueIndexesOnSoftDelete`: its `Down` recreates the unfiltered unique indexes and fails
once a deleted email or `(job, candidate)` pair has been reused. Remove or merge those duplicates before
rolling back.

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
