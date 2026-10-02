# RBAC Phase 3: HiringManager Scoping Implementation Plan

> **For agentic workers:** run through `/ats-ship docs/plans/2026-10-02-rbac-phase-3-hiring-manager-scoping.md`.
> Project overrides apply: implementers never commit or stage; they leave a working-tree diff.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A HiringManager sees and acts only on the jobs they are assigned to (and those jobs' applications,
candidates and CVs), and the role becomes assignable on the Users screen.

**Architecture:** Jobs get a hiring team (`JobHiringManager` link table, several managers per job). A new
permission `jobs.viewall` (Owner, Recruiter, Viewer) decides who is unrestricted; a user with `jobs.view`
but without `jobs.viewall` (the HiringManager) is scoped. One Application service, `IJobScope`, answers
"is this user restricted, and to which user id", and every read path applies it explicitly: read models
filter lists and counts, and the by-id choke points return not-found for out-of-scope ids. No global query
filter on `Job`: manage paths, the worker and integration counts must keep seeing every job.

**Tech Stack:** .NET 10 MVC, EF Core (SQL Server), xUnit, Playwright.

**Decisions (developer, 2 October 2026):** several hiring managers per job; the HiringManager dashboard is
scoped to their own jobs and the tenant activity feed (audit data) is hidden for them.

---

## Rules (source of truth)

| Rule | Detail |
|---|---|
| Who is scoped | Signed-in user with `jobs.view` and without `jobs.viewall` (today: HiringManager). Decided by permission, never by role name |
| Unrestricted | Owner, Recruiter, Viewer (`jobs.viewall`). Anonymous callers (career site, worker) never reach the scoped paths; `IJobScope` reports them unrestricted and this is documented and tested |
| Own jobs | Jobs where a `JobHiringManager` row links the job to the user (soft-deleted jobs stay hidden by the existing filter) |
| Own applications | Applications whose job is an own job |
| Own candidates | Candidates with at least one application on an own job. Candidate rows show only data from own-job applications (latest job, stage, count) |
| Out of scope by id | Job, board, application details/card, candidate page, CV download: **404**, the same response as a missing id (no existence oracle) |
| Board move | The application must be in scope **and** belong to the route's job (also fixes the existing gap for every role: today the route `jobId` is never checked against the application) |
| Lists and counts | Jobs list, Candidates list, global search, dashboard metrics, sidebar badges, attention bell: own data only |
| Activity feed | Hidden for scoped users |
| Assignment | Only users with `jobs.manage` edit a job's hiring team. Assignable people: active users whose role is HiringManager, in the same tenant (validated server-side) |
| Assignable role | `AtsRole.Assignable` gains HiringManager |
| Tenancy | `JobHiringManager` is a `TenantEntity`; all queries stay on the tenant-filtered context; no `IgnoreQueryFilters` |

## Out of scope
- Notifications to hiring managers (email integration later).
- Hiring managers editing jobs, adding candidates or removing applications (not in the approved matrix).
- Interview scorecards / feedback.

## File map (main)
| Area | Files |
|---|---|
| Domain | `Authorization/AtsPermission.cs`, `RolePermissions.cs`, `Enums/AtsRole.cs`, new `Entities/JobHiringManager.cs`, `Entities/Job.cs` |
| Infrastructure | new `Persistence/Configurations/JobHiringManagerConfiguration.cs`, `AtsDbContext.cs`, migration `AddJobHiringManagers`, `Jobs/JobListQuery.cs`, `Candidates/CandidateListQuery.cs`, `Applications/ApplicationCardQuery.cs`, `Search/GlobalSearchService.cs`, `Dashboard/DashboardService.cs`, `Shell/ShellSummaryService.cs`, repositories |
| Application | new `Jobs/IJobScope.cs` + `JobScope.cs`, `Jobs/JobService.cs` + `JobModels.cs`, `Applications/ApplicationService.cs`, `Candidates/CandidateService.cs` |
| Web | `Controllers/{Jobs,Board,Applications,Candidates,Resume,Search,Dashboard}Controller.cs`, `Views/Jobs/Form.cshtml`, `Views/Board/Index.cshtml`, `Views/Dashboard/Index.cshtml`, `Views/Users/Edit.cshtml`, `Models/JobEditViewModel.cs` |
| Tests | `tests/Ats.Tests/**` (scope, guards, assignment, permissions, users), new `tests/e2e/hiring-manager.spec.ts`, `users.spec.ts` role options |

---

### Task 1: `jobs.viewall` permission and HiringManager assignable

**Model:** opus (authorization). **Playwright:** `users.spec.ts` (role options).

- [x] Tests first: `RolePermissionsTests` expected sets gain `JobsViewAll` for Owner, Recruiter and Viewer,
  not HiringManager; replace `HiringManager_is_not_assignable_until_it_is_scoped` with "Assignable is
  Owner, Recruiter, HiringManager, Viewer"; `UserServiceTests` `Create_rejects_HiringManager` and
  `Role_cannot_be_changed_to_HiringManager` become "accepts HiringManager".
- [x] `AtsPermission.JobsViewAll = "jobs.viewall"` (in `All`); grant to Owner (via All), Recruiter and Viewer.
- [x] `AtsRole.Assignable` adds HiringManager (comment updated: scoped since phase 3).
- [x] `users.spec.ts`: role options now Owner, Recruiter, HiringManager, Viewer (check the select's display text).
- [x] Verify: build 0 warnings, `dotnet test`, format, the Playwright spec above. Mutation-check the
  `JobsViewAll` grant.

### Task 2: `JobHiringManager` entity and migration

**Model:** opus (entity, migration). **Playwright:** none.

- [x] `JobHiringManager : TenantEntity { int JobId; int UserId; }`; `Job.HiringManagers` collection.
- [x] Configuration: unique `(TenantId, JobId, UserId)`; index `(TenantId, UserId)` for the scope lookups;
  FK to `Jobs` with cascade delete; FK to `Users` with **restrict** (users are deactivated, never deleted).
- [x] `dotnet ef migrations add AddJobHiringManagers ...` (create only). Inspect: one table, two indexes,
  FKs, nothing else.
- [x] Verify build, tests, format.
- [x] **Manual gate (lead):** print the `database update` command and wait for the developer.

Note: EF's foreign-key convention also created `IX_JobHiringManagers_JobId` and `IX_JobHiringManagers_UserId`
beside the two configured indexes. Accepted; consistent with the existing tables.

### Task 3: `IJobScope` and the by-id choke points

**Model:** opus (authorization, tenant-scoped data). **Playwright:** `journeys.spec.ts`, `application-remove.spec.ts`.

- [x] `IJobScope` (Application): `bool IsRestricted`, `int? UserId`, `IQueryable`-free. `JobScope` reads
  `ICurrentUser` and `RolePermissions.Has(role, JobsView)` / `Has(role, JobsViewAll)`. Restricted only when
  authenticated, has `jobs.view` and lacks `jobs.viewall`. Registered in `AddAtsInfrastructure` next to the
  other Application services (scoped).
- [x] A repository method `Task<bool> IsAssignedAsync(int jobId, int userId)` and
  `Task<bool> CandidateHasApplicationOnJobsOfAsync(int candidateId, int userId)` (names may vary; keep them
  tenant-filtered).
- [x] Guards (return the same not-found result as a missing id):
  - `JobService.GetAsync(id)` for viewing (keep an unscoped path for manage operations if needed, named so
    it cannot be confused: e.g. `GetForManageAsync`, used only behind `jobs.manage`).
  - `ApplicationService`: `GetJobAsync`, `GetAsync`, `GetWithCandidateAsync`, `MoveStageAsync` (scope **and**
    application.JobId == route jobId; route job id passed in), used by Board, Applications, Resume.
  - `CandidateService.GetAsync` for viewing (Candidates/Edit GET) and the CV path.
  - Controllers map not-found to 404 (Board Index, Jobs Edit GET, Applications Details/Card,
    Candidates Edit GET, Resume Download).
- [x] Unit tests with fakes (`FakeCurrentUser` role HiringManager / Recruiter, `FakeJobRepository`,
  `FakeApplicationRepository`): restricted user gets not-found for unassigned job/application/candidate and
  succeeds for assigned ones; unrestricted roles unaffected; anonymous unrestricted; move with mismatched
  route job id fails for every role. Mutation-check the main guard.
- [x] Verify build, tests, format, the Playwright specs above.

### Task 4: Scope the read models (lists, search, dashboard, shell)

**Model:** opus (tenant-scoped queries). **Playwright:** `smoke.spec.ts`, `audit.spec.ts` (paging),
`journeys.spec.ts`. Shared files touched (list queries, shell): e2e-verifier runs the full suite once.

- [x] One shared Infrastructure helper applies the scope to `IQueryable<Job>` / `IQueryable<JobApplication>`
  (e.g. `.Where(j => db.JobHiringManagers.Any(h => h.JobId == j.Id && h.UserId == uid))`), so every read
  model uses the same predicate.
- [x] `JobListQuery`: list, totals, stage counts, applicant names.
- [x] `CandidateListQuery`: candidates with an own-job application; latest job/stage/count from own-job
  applications only.
- [x] `ApplicationCardQuery`: not-found when out of scope.
- [x] `GlobalSearchService`: jobs, candidates, referral-code applications scoped.
- [x] `DashboardService`: every metric, chart and attention item scoped; activity feed omitted when
  restricted (the view hides the section).
- [x] `ShellSummaryService`: sidebar badges and attention bell scoped.
- [x] Any other `_db.Jobs` / `_db.Applications` reader reachable by a scoped user (check the list in the
  research notes: integration/organisation/pipeline readers are behind other permissions).
- [x] Tests: the predicate helper as a pure `IQueryable` filter over in-memory data (like `UserListFilter`);
  e2e covers the SQL side in Task 6.
- [x] Verify build, tests, format, the Playwright specs above.

### Task 5: Hiring team assignment UI

**Model:** opus (service rules + tenant-scoped validation), UI by the same agent. **Playwright:** `journeys.spec.ts`,
`form-validation.spec.ts`, `a11y.spec.ts`, `responsive.spec.ts`.

- [x] `JobInput` gains `IReadOnlyList<int> HiringManagerIds`; `JobService.CreateAsync`/`UpdateAsync` replace
  the job's team; every id must be an **active HiringManager in the tenant** (validated through a
  repository read on the tenant-filtered context); duplicates ignored. Unit tests with fakes; mutation-check.
- [x] Job form: a "Hiring team" field (checkbox list styled as selectable chips with avatar and name, with
  a short empty state "No hiring managers yet. Add one on the Users screen." linking to Users when the
  user can manage users). Read-only form (no `jobs.manage`) shows the team as chips.
- [x] Board header shows the hiring team (avatars with names on hover/focus).
- [x] Users Edit page: for a HiringManager, an "Assigned jobs" card listing their jobs (links to the board).
- [x] Audit: job create/update summaries mention team changes (names, not ids).
- [x] Verify build, tests, format, the Playwright specs above.

### Task 6: End-to-end HiringManager coverage

**Model:** sonnet (tests only). **Playwright:** new `hiring-manager.spec.ts` (run twice), `users.spec.ts`.

`tests/e2e/hiring-manager.spec.ts` (serial, deterministic fixtures created through the UI, all test users
deactivated at the end):
- [x] Owner creates a HiringManager, two jobs A and B with one candidate each, and assigns the manager to A.
- [x] HM: Jobs list shows A only; sidebar Jobs badge counts own open jobs; Candidates list shows A's
  candidate only; global search for B's title or candidate returns nothing; dashboard numbers match A only
  and the activity feed is absent.
- [x] HM: board of A works (drawer opens, CV download for A works, stage move on A works); board of B,
  Jobs/Edit B, Applications Details of B's application, Candidates/Edit of B's candidate, CV of B: **404**.
- [x] HM: a forged move POST for B's application is rejected; a move with A's route job id but B's
  application id is rejected.
- [x] Owner removes the manager from A: the HM's Jobs list is empty on the next request.
- [x] Verify the spec twice, plus `users.spec.ts`.

### Task 7: Documentation

**Model:** sonnet. **Playwright:** none.
- [x] Authorization skill: scoping model (`jobs.viewall`, `IJobScope`, own jobs/applications/candidates,
  404 rule, board move job match), assignment rules, HiringManager now assignable.
- [x] Entities skill: `JobHiringManager`. Pipeline skill: board move job match.
- [x] Multi-tenancy skill: note that job scoping is separate from tenancy and never uses the global filter.
- [x] RBAC spec: phase 3 done; matrix "own jobs" notes resolved.
- [x] `CLAUDE.md` skill index if scope grew.

## After the last task
Review follow-ups (if any), the full-suite gate, then the whole-change review (per `/ats-ship`).

## Definition of done
- `dotnet build Ats.slnx` 0 warnings, `dotnet test` green, `dotnet format --verify-no-changes` clean
- Targeted e2e per task; full suite once after Task 4 (shared files) and once at the end, green
- Migration `AddJobHiringManagers` created; `database update` in the close-out
