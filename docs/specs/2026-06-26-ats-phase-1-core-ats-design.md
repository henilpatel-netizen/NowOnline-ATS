# Design Spec: ATS Phase 1 - Core ATS

- Date: 2026-06-26
- Status: Proposed (awaiting review)
- Author: Henil Patel (with Claude)
- Related: `2026-06-26-ats-product-design.md` (Sections 7, 7.1, 7.2, 8, 11.1, 12 Phase 1),
  `2026-06-26-ats-ui-baseline-design.md` (UI pattern).

---

## 1. Purpose and scope

Build the core ATS workflow on top of the Phase 0 foundation: configurable hiring pipelines,
departments and locations, jobs with a publish lifecycle, candidates, applications, and a kanban
board for moving candidates through stages with full ordered history.

Phase 1 records stage-move history. It does NOT emit anything to ReferralTool and does not build the
vacancy feed; the outbox, emission, and feed are Phase 3. The board records an `ApplicationEvent`
for every move (forward and backward); Phase 3 decides what to emit from that history.

### In scope
Pipeline template CRUD (stages, order, terminal outcomes, stage-to-status mapping); Department and
Location CRUD; Job CRUD with Draft/Published/Closed lifecycle and `ExternalRef` generation; Candidate
and Application aggregates; candidate list and per-job kanban board; stage moves with `RowVersion`
optimistic concurrency and `ApplicationEvent` history; soft delete for Job/Candidate/Application; the
one-active-application-per-(candidate, job) rule.

### Out of scope (deferred)
- ReferralTool emission, outbox, worker, and the CatsOne vacancy feed (Phase 3).
- Public career site, resume upload, and `IFileStore` (Phase 2). `Candidate.ResumeFileKey` exists but
  stays null in Phase 1 manual-add.
- Email notifications, audit views, dashboards (Phase 4).
- A formal `Manage` MVC area. Phase 1 controllers stay at the root, consistent with Phase 0
  (`Account`, `Dashboard`) and the UI baseline. The `Careers` area is introduced in Phase 2 when the
  public slug-routed site forces the split.

---

## 2. Locked decisions

| Decision | Choice |
|---|---|
| Phase 1 slicing | One spec, two implementation plans (A: config + jobs, B: candidates + board) |
| Soft delete | `ISoftDeletable { bool IsDeleted }`; extend the global filter to AND `!IsDeleted`; services set the flag |
| Stage moves on the board | SortableJS drag-and-drop posting via htmx, plus an accessible per-card move menu fallback |
| Job.ExternalRef | Per-tenant counter `JOB-{n}` from `TenantSettings.LastJobNumber`, stable, never reused |
| ReferralTool emission | Not in Phase 1. History (`ApplicationEvent`) only; emission is Phase 3 |
| MVC areas | Controllers stay at root in Phase 1; `Careers` area added in Phase 2 |

---

## 3. Domain model

### 3.1 New common abstraction
`Ats.Domain/Common/ISoftDeletable.cs`:

```csharp
public interface ISoftDeletable { bool IsDeleted { get; set; } }
```

### 3.2 Enums
- `JobStatus { Draft = 0, Published = 1, Closed = 2 }`
- `EmploymentType { FullTime = 0, PartTime = 1, Contract = 2, Internship = 3, Temporary = 4 }`
- `ApplicationStatus { Active = 0, Hired = 1, Rejected = 2, Withdrawn = 3 }`

(`StageOutcome` already exists from Phase 0.)

### 3.3 Entities
- `Department : TenantEntity` - `Name` (required, max 120).
- `Location : TenantEntity` - `Name` (required, max 120), `City` (max 120). `City` is what the Phase 3
  feed maps to `location.city`.
- `Job : TenantEntity, ISoftDeletable` - `Title` (required, max 200), `Description` (text),
  `DepartmentId?`, `LocationId?`, `EmploymentType`, `PipelineTemplateId` (required), `Status`
  (default Draft), `PublishedAt?`, `ExternalRef` (required, max 36, unique per tenant), `IsDeleted`.
- `Candidate : TenantEntity, ISoftDeletable` - `FirstName` (max 100), `LastName` (max 100), `Email`
  (required, max 256), `Phone` (max 40), `ResumeFileKey?` (null until Phase 2), `IsDeleted`.
- `Application : TenantEntity, ISoftDeletable` - `CandidateId`, `JobId`, `CurrentStageId`,
  `SourceCode?` (max 36, captured in Phase 2), `AppliedAt`, `Status` (default Active),
  `RowVersion` (byte[], `[Timestamp]`), `IsDeleted`.
- `ApplicationEvent : TenantEntity` - `ApplicationId`, `FromStageId?`, `ToStageId`, `OccurredAt`,
  `MovedByUserId?`. Append-only history.

### 3.4 ExternalRef generation
`TenantSettings` gains `int LastJobNumber` (default 0). On job create, `JobService` increments it
inside the same transaction as the insert and sets `ExternalRef = $"JOB-{LastJobNumber}"`. Because the
counter only increases, refs are stable and never reused even after a job is soft-deleted.

---

## 4. Persistence and tenancy

- Soft delete: `AtsDbContext.OnModelCreating` already loops entity types to add the tenant filter. Extend
  the builder so that for types implementing `ISoftDeletable`, the filter body becomes
  `e.TenantId == GetTenantIdOrZero() && !e.IsDeleted`. This keeps a single combined filter per entity
  and preserves fail-closed tenancy. Hard delete is never used for these aggregates; services set
  `IsDeleted = true`.
- Concurrency: `Application.RowVersion` mapped as a SQL `rowversion`. Stage moves use it for optimistic
  concurrency so two recruiters cannot double-move the same application; a concurrency failure surfaces
  as a friendly "reload and retry" message.
- Indexes/uniqueness:
  - `Job`: unique `(TenantId, ExternalRef)`.
  - `Candidate`: unique `(TenantId, Email)` (dedupe per tenant).
  - `Application`: unique `(TenantId, JobId, CandidateId)` to back the one-active-application rule.
  - `ApplicationEvent`: index `(TenantId, ApplicationId, OccurredAt)`.
  - `PipelineStage`: index `(TenantId, PipelineTemplateId, Order)` (added now; useful for board columns).
- New EF configurations live in `Ats.Infrastructure/Persistence/Configurations` following the existing
  one-class-per-aggregate pattern.

---

## 5. Application layer

Each aggregate gets a service interface in `Ats.Application` plus a repository interface, with the
repository implemented in `Ats.Infrastructure` (the established Phase 0 pattern: service ->
repository -> EF). Validation uses FluentValidation in the Application layer.

- `IPipelineTemplateService` - list, create, rename, delete templates; add/remove/reorder stages;
  set terminal flag + `TerminalOutcome`; set `ReferralStatusOverride` per stage. A template in use by a
  job cannot be deleted (guarded).
- `IDepartmentService`, `ILocationService` - straightforward CRUD with soft delete.
- `IJobService` - create (assign `ExternalRef`, require a pipeline template), edit, and lifecycle
  transitions: Draft -> Published (sets `PublishedAt`), Published -> Closed, Closed -> Published
  (reopen). Invalid transitions are rejected.
- `ICandidateService` - create-or-match by `(TenantId, Email)`, edit, list. Manual add in Phase 1
  (no resume).
- `IApplicationService`:
  - `AddToJob(jobId, candidate)` - create/match the candidate, then create an `Application` at the
    job's pipeline first stage (lowest `Order`), write the initial `ApplicationEvent`
    (`FromStageId = null`, `ToStageId = firstStage`). If an application already exists for
    (candidate, job), return it instead of creating a duplicate.
  - `MoveStage(applicationId, toStageId, rowVersion)` - validate the target stage belongs to the
    application's job's template; update `CurrentStageId`; append an `ApplicationEvent`; if the target
    stage is terminal, set `Application.Status` to Hired/Rejected per `TerminalOutcome`; use
    `RowVersion` for concurrency. Backward and forward moves are both allowed and both recorded.
  - Board and list queries scoped to a job.

---

## 6. Web layer

Thin controllers at the root, Razor views using the UI baseline (sidebar, `_PageHeader`, `_Alerts`,
Bootstrap, tokens). New sidebar `NavItem`s: Jobs, Pipelines, Candidates, and a Settings group for
Departments/Locations.

- `PipelinesController` - template list, create/edit (stages editor with add/remove/reorder, terminal
  outcome, status-override field).
- `DepartmentsController`, `LocationsController` - list + create/edit.
- `JobsController` - list (filter by status), create/edit, and lifecycle actions (publish/close/reopen)
  as POST actions with `TempData` confirmation.
- `CandidatesController` - list + create/edit; "add to job" action.
- `BoardController` (or `JobsController.Board`) - per-job kanban: columns are the template stages in
  order, cards are active applications in each stage. Drag-and-drop is handled by SortableJS; the drop
  fires an htmx POST to a `MoveStage` endpoint that returns the updated card/column partial. Each card
  also has a "Move to" menu listing stages as an accessible fallback. The move form carries the
  application's `RowVersion`; a concurrency conflict re-renders with a reload prompt.

All POST actions rely on the global antiforgery validation already configured.

---

## 7. Two implementation plans

### Plan A - Config and Jobs
`ISoftDeletable` + filter extension; `JobStatus`/`EmploymentType` enums; `Department`, `Location`,
`Job` entities; `TenantSettings.LastJobNumber`; EF configs + one migration (created, developer
applies); `IPipelineTemplateService`, `IDepartmentService`, `ILocationService`, `IJobService` with
repositories; Pipelines, Departments, Locations, and Jobs screens; sidebar entries.
Exit: a recruiter can define a pipeline template, manage departments/locations, and create then publish
a job that receives a stable `ExternalRef`.

### Plan B - Candidates, Applications, Board
`Candidate`, `Application`, `ApplicationEvent` entities; `ApplicationStatus` enum; `RowVersion`;
EF configs + one migration (created, developer applies); `ICandidateService`, `IApplicationService`
with repositories; candidate list/create/edit; per-job kanban board with drag-and-drop + fallback and
the move-history trail.
Exit: a recruiter can add a candidate to a published job and move them through stages, with each move
recorded as an ordered `ApplicationEvent`.

---

## 8. Error handling

- Invalid lifecycle transitions, deleting an in-use pipeline template, and moving to a stage outside
  the application's template are rejected with a friendly message (no exception to the user).
- Optimistic concurrency failures on stage move re-render the board with a "this application changed,
  reload" notice.
- Model validation via FluentValidation + tag-helper summaries, consistent with the UI baseline.

---

## 9. Verification

Build + run (no test project, per repo convention). Manual checks:
- Plan A: create a pipeline template with stages and a terminal stage; create departments/locations;
  create a job, confirm a `JOB-{n}` ExternalRef; publish it and confirm `PublishedAt`; soft-delete a
  draft and confirm it disappears from lists but the next job's number does not reuse the deleted ref.
- Plan B: add a candidate to a published job (lands in the first stage with an initial event); move the
  candidate forward and backward via drag-and-drop and via the fallback menu; confirm `ApplicationEvent`
  rows are ordered and a terminal stage sets the application status; confirm re-adding the same email to
  the same job does not duplicate.
- Tenancy: all new lists are tenant-filtered; soft-deleted rows are hidden.

---

## 10. Documentation maintenance (per spec Section 16)

Phase 1 adds skills for the domains it builds. After Plan A and Plan B land:
- Add `.claude/skills/entities/SKILL.md` (the Phase 1 aggregates, soft delete, ExternalRef) and
  `.claude/skills/pipeline/SKILL.md` (templates, stages, stage-to-status mapping, board moves and
  history).
- Refresh the `CLAUDE.md` skill-index and keep `docs/specs` and `docs/plans` current.

---

## 11. Notes

- Restrictions unchanged: the AI does not commit, apply migrations, or deploy. Phase 1 creates two
  migrations; the developer applies them.
- Em dashes and emoji are avoided in all generated content per the working conventions.
