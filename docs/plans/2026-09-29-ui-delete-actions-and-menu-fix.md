# UI: row-menu fix and missing delete actions

> Run with `/ats-ship docs/plans/2026-09-29-ui-delete-actions-and-menu-fix.md`. Implementers never commit.

**Goal:** fix the clipped row action menus, and add the delete/remove actions the back office is missing
(departments, locations, candidates, candidate-from-job).

**Decisions (developer, 29 September 2026):** candidate delete is a **soft delete that cascades** to the
candidate's applications (personal data stays in the database; a GDPR erasure feature is separate). Delete
permissions stay as today: any signed-in user of the tenant (same as Jobs and Pipelines). Team/user
management is out of scope.

## UI audit (29 September 2026)

| Area | Today | Action |
|---|---|---|
| Jobs row menu | Clipped by `.ats-card-flush { overflow: hidden }`; every row's `.dropdown` has `z-index: 2`, so later rows paint over an open menu | Task 1 |
| Candidates row menu ("Add to job") | Same pattern, same bug | Task 1 |
| Departments, Locations | Service + controller `Delete` exist (blocked when a job uses them); no button in Organisation | Task 2 |
| Candidates | No delete | Tasks 3 + 4 |
| Applications | No way to remove a candidate from a job | Tasks 3 + 5 |
| Pipelines | Edit, delete, stage delete exist | none |
| Team / users | Roles exist, no management UI | out of scope |

## Task 1: Row action menus are never clipped or covered

**Files:** `src/Ats.Web/wwwroot/css/ats-components.css`, `src/Ats.Web/Views/Jobs/Index.cshtml`,
`src/Ats.Web/Views/Candidates/Index.cshtml`, a Playwright spec under `tests/e2e/`.

- [x] Make an open row menu render above every other row and outside the card's clipping, for any number of
  rows (1, 2, 3, many), on the first, middle and last row. Root causes: `.ats-card-flush { overflow: hidden }`
  (keep its rounded corners) and the shared `z-index: 2` on `.ats-trow--link .dropdown` (all rows equal, so
  later rows win). Prefer one shared fix (for example a raised z-index for the row whose menu is open, plus
  Popper `strategy: fixed` via `data-bs-popper-config` or an equivalent) over per-page hacks. No inline
  `grid-template-columns`, no raw hex.
- [x] Playwright: with at least two jobs, open the menu of the **last** row and assert every item is visible
  and is the topmost element at its centre (`elementFromPoint`), then click Edit and land on the edit page.
  Same check for the Candidates "Add to job" menu.
- [x] Existing a11y, responsive and boosted-nav specs stay green.

## Task 2: Delete departments and locations from the Organisation page

**Files:** `src/Ats.Web/Views/Organisation/Index.cshtml`, `tests/Ats.Tests/...` (service tests).

- [x] Add a delete control next to each department and location edit icon: a POST form to the existing
  `Departments/Delete/{id}` and `Locations/Delete/{id}` with `hx-confirm` ("Delete this department?"),
  accessible name `Delete <name>`, Material Symbol `delete`, danger styling from the design tokens.
- [x] The existing rule stays: in use by a job -> the page shows the service's error message; otherwise
  "Department deleted." / "Location deleted." Confirm the controllers redirect back to Organisation and set
  the result message.
- [x] Unit tests (hand-rolled fakes) for `DepartmentService.DeleteAsync` and `LocationService.DeleteAsync`:
  not found, referenced by a job (not removed), success (removed). Add only what is missing.

## Task 3: Unique indexes ignore soft-deleted rows (migration)

**Files:** `CandidateConfiguration.cs`, `JobApplicationConfiguration.cs`, new migration.

- [x] `IX (TenantId, Email)` on Candidates and `IX (TenantId, JobId, CandidateId)` on Applications become
  filtered unique indexes `WHERE [IsDeleted] = 0`, so a deleted candidate's email can be used again and a
  removed candidate can be added to the same job again.
- [x] Found in Task 2: the "in use by a job" checks ignore soft-deleted jobs, but the foreign keys
  (`OnDelete(Restrict)` in `JobConfiguration`) still block the delete, so deleting a department, location
  or pipeline whose only jobs are soft-deleted fails with a 500. Fix: `Job.DepartmentId` and
  `Job.LocationId` FKs become `OnDelete(SetNull)`. `PipelineTemplateId` is required, so pipeline delete
  instead catches the FK failure in Infrastructure and returns `OperationResult.Fail("This pipeline is
  still used by deleted jobs and cannot be removed.")`. No `IgnoreQueryFilters`.
- [x] Create (do not apply) migration `FilterUniqueIndexesOnSoftDelete` (includes the FK change). Manual command for the developer:
  `dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`.

## Task 4: Delete a candidate (soft delete, cascades to applications)

**Files:** `ICandidateService`/`CandidateService`, `ICandidateRepository` + implementation,
`CandidatesController`, `Views/Candidates/Index.cshtml` (row menu), `Views/Candidates/Form.cshtml` (edit
page), tests.

- [x] `CandidateService.DeleteAsync(id)`: not found -> fail "Candidate not found."; otherwise set
  `IsDeleted = true` on the candidate and on all its applications in one save (use
  `IApplicationRepository.InTransactionAsync` or a single SaveChanges; respect the EF execution strategy
  rule). No hand-set TenantId, no filter bypass.
- [x] `POST Candidates/Delete/{id}`: audit `CandidateDeleted`, result message "Candidate deleted.",
  redirect to the Candidates list.
- [x] UI: "Delete" (danger) in the Candidates row menu (menu shows even when there are no published jobs)
  and a delete button on the candidate edit page, both with `hx-confirm` "Delete this candidate and remove
  them from all jobs?".
- [x] After delete the candidate disappears from the list, search, board and dashboard counts (global
  filter); the same email can apply again on the career site (Task 3).
- [x] Unit tests: not found; candidate and all their applications soft-deleted; other candidates untouched.
  Mutation-check the cascade.
- [x] Note in the entities skill: ReferralTool is not told about a deleted candidate (no API); queued
  outbox messages for its applications still send.

## Task 5: Remove a candidate from a job (soft delete the application)

**Files:** `IApplicationService`/`ApplicationService`, repository, `ApplicationsController` (or
`BoardController`), `Views/Shared/Partials/_CandidateDrawer.cshtml`, tests.

- [x] `ApplicationService.RemoveAsync(applicationId)`: not found -> fail; otherwise `IsDeleted = true` on the
  application only (the candidate stays).
- [x] POST endpoint with audit `ApplicationRemoved`, result message "Candidate removed from this job.",
  redirect to that job's board.
- [x] Drawer action "Remove from job" (danger) with `hx-confirm` "Remove this candidate from the job? Their
  history on this job is hidden." Respect the htmx inheritance rules (override `hx-target`/`hx-select` if
  the drawer lives inside `#ats-content`).
- [x] Unit tests: not found; application soft-deleted; candidate untouched.

- [x] Found in Task 4 review: `wwwroot/js/site.js` disables a form's submit button in the capture phase,
  before htmx shows the `hx-confirm` dialog, so cancelling the dialog leaves the button (Delete, Close,
  Publish, Remove from job) disabled until reload. Disable only when the request actually goes out (for
  example on `htmx:beforeRequest` for htmx/boosted forms, native submit otherwise), and re-enable on
  `htmx:afterRequest` failures. Playwright: cancel the confirm, then the button still works.

## Done
Build 0 warnings, tests green, format clean, tenancy PASS (Tasks 3-5), conventions PASS, e2e PASS (Tasks
1, 2, 4, 5). Update the entities and ui skills.
