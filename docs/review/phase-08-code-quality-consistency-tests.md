# Phase 8 — Code Quality, Consistency & Tests

Debt that doesn't block launch but decides whether the codebase stays healthy over 10 years. The test
gap (QUAL-1) is the most important — do it alongside the earlier phases so their changes are covered.

Raises: Maintainability, Architecture. Verified against `c7f4614`.

---

### [ ] QUAL-1 · Unit-test the business logic — Priority: High · Effort: M
**Files:** `tests/Ats.Tests` covers only pure helpers (`DashboardMath`, `RelativeTime`,
`AvatarPalette`, brand colour, `FeedPullThrottle`). Untested, high-risk logic:
`ApplicationService.MoveStageAsync` terminal-outcome mapping + duplicate-apply guard
(`ApplicationService.cs:108,80`), `PipelineTemplateService.SaveAsync` stage add/rename/reorder/remove
diff (`PipelineTemplateService.cs:24`), `JobService` status transitions.
**Problem:** The invariants most likely to regress have zero coverage — yet these services depend only
on repository **interfaces**, so they're trivially testable with fakes (no DB).
**Fix:** Add xUnit tests with hand-rolled/in-memory fakes covering: hire/reject/no-op stage moves +
status mapping, RowVersion-conflict path, duplicate-apply dedup, "pipeline has no stages", pipeline
stage-diff, job publish/close/delete transitions, and the corrected offer-acceptance KPI (DATA-5).
Add tests for each earlier-phase change as it lands.
**Acceptance:** Core rules are covered; coverage is meaningful, not just the helpers.
**Verify:** `dotnet test` green with new suites; deliberately breaking a rule turns a test red.

---

### [ ] QUAL-2 · Delete dead, divergent search code — Priority: High · Effort: S
**Files:** `IJobService.SearchAsync`/`JobRepository.SearchAsync` (`JobService.cs:27`,
`JobRepository.cs:16`) and the candidate equivalents (`CandidateService.cs:23`,
`ICandidateRepository.cs:8`) — **unused** (screens call `JobListQuery`/`CandidateListQuery`) and they
already filter **differently** (`Contains` vs `EF.Functions.Like`).
**Problem:** Two "search" implementations that disagree; a future fix patches the wrong one.
**Fix:** Remove the unused `SearchAsync` from services, repositories, and their interfaces. Keep the
`*Query` read models as the single search path.
**Acceptance:** One search implementation; build clean; lists unaffected.
**Verify:** Grep shows no remaining `SearchAsync` on Job/Candidate services; `dotnet build`/tests green.

---

### [ ] QUAL-3 · Consistent read-model pattern (thin controllers) — Priority: Medium · Effort: M
**Files:** `BoardController.BuildBoardAsync` (`BoardController.cs:70-114`) does aggregation inline;
`IntegrationController.Index` (`:26-49`) builds a health banner via `ViewData` magic strings + 4
separate count round-trips; `TestConnection` (`:66-89`) holds orchestration + HTTP interpretation.
**Problem:** "Controllers stay thin / use a read service" holds everywhere except the two most complex
screens; `ViewData` string keys break silently on change.
**Fix:** Add `IBoardQuery`/`BoardViewService` and an `IIntegrationHealthQuery` returning typed models
(mirroring `JobListQuery` etc.); move `TestConnection` logic into `IIntegrationSettingsService`.
**Acceptance:** Board and Integration use typed read models; controllers are thin.
**Verify:** Controllers contain no aggregation/`ViewData` banner logic; screens render identically.

---

### [ ] QUAL-4 · Relocate `OperationResult` to Common — Priority: Medium · Effort: S
**Files:** defined in `src/Ats.Application/Departments/DepartmentService.cs:5`, imported everywhere with
`using Ats.Application.Departments; // OperationResult` (`JobService.cs:2`, `CandidateService.cs:2`,
`ApplicationService.cs:3`, `PipelineTemplateService.cs:1`, `LocationService.cs:1`;
`BoardController.cs:51` fully-qualifies it).
**Problem:** Every service takes a wrong dependency on the `Departments` feature namespace for a
cross-cutting primitive.
**Fix:** Move `OperationResult` to `src/Ats.Application/Common/` (beside `PagedResult`); update
namespaces; delete the explanatory comments. Mechanical.
**Acceptance:** `OperationResult` lives in `Common`; no `// OperationResult` comments remain.
**Verify:** `dotnet build` clean; grep for the old comment → none.

---

### [ ] QUAL-5 · De-duplicate CRUD + controller result/audit boilerplate — Priority: Low · Effort: M
**Files:** `DepartmentService`/`LocationService` + their repositories are structurally identical (Location
adds only `City`); the `TempData[result.Succeeded ? "Success":"Error"] = …` + paired
`_audit.LogAsync(...)` idiom is copy-pasted across JobsController, PipelinesController,
CandidatesController, IntegrationController, BoardController.
**Problem:** DRY erosion in the parts most likely to grow (more lookup types, more CRUD actions);
hand-typed audit action/entity strings are easy to mistype.
**Fix:** Extract a small named-lookup base (e.g. `NamedLookupRepository<T>` + shared "referenced by
job?" guard) and a thin controller helper `RedirectWithResult(OperationResult, successMsg, action)`
that optionally funnels the audit call. Keep it lightweight — **no** mediator/behaviour pipeline.
**Acceptance:** The two most-copied blocks are single-sourced; adding a lookup or CRUD action is less
boilerplate.
**Verify:** New helper used by all mutating actions; Department/Location share the base; tests green.

---

### [ ] QUAL-6 · Move `HttpTenantContext` out of shared Infrastructure — Priority: Low · Effort: S
**Files:** `src/Ats.Infrastructure/Tenancy/HttpTenantContext.cs` (needs `IHttpContextAccessor`);
`Ats.Infrastructure.csproj` carries `FrameworkReference Microsoft.AspNetCore.App` partly for this; the
Worker does `RemoveAll<ITenantContext>()` to undo it.
**Problem:** A host-specific (HTTP) implementation lives in shared Infrastructure, forcing a web
dependency on all hosts and a remove-and-replace hack in the Worker.
**Fix:** Move `HttpTenantContext` to `Ats.Web` and register it in the web host; keep
`WorkerTenantContext` in the Worker. Infrastructure stops owning an HTTP concern.
**Acceptance:** Infrastructure no longer references an HTTP-only tenant context; both hosts wire their
own; the Worker's `RemoveAll` hack is removed.
**Verify:** `dotnet build`; web + worker both resolve `ITenantContext` correctly at runtime.

---

### [ ] QUAL-7 · `ApplicationsController.Details` single-row fetch — Priority: Low · Effort: S
**Files:** `src/Ats.Web/Controllers/ApplicationsController.cs:21-27`
(`(await _service.ListForJobAsync(app.JobId)).FirstOrDefault(a => a.Id == id)` — loads the whole job's
applications to attach one candidate, then mutates `app.Candidate`).
**Problem:** List-then-find for a single entity + entity mutation in the controller; grows with job
size.
**Fix:** Add `IApplicationRepository.GetWithCandidateAsync(id)` and use it.
**Acceptance:** One targeted query; no controller-side entity mutation.
**Verify:** Details page renders identically; query count drops to one.

---

## Exit criteria
- [ ] Core business logic is unit-tested; earlier-phase changes are covered.
- [ ] Dead search removed; read-model pattern consistent; `OperationResult` in `Common`.
- [ ] CRUD/audit duplication reduced; `HttpTenantContext` relocated; Details uses a targeted query.
- [ ] `dotnet build` clean (ideally `TreatWarningsAsErrors=true` from FND-5), `dotnet test` green.
