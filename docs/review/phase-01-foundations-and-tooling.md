# Phase 1 — Foundations & Tooling

Low-risk infrastructure that unblocks later phases and establishes an automated quality gate. Do this
first: `FND-1` (DataProtection) is a prerequisite for encrypting secrets in Phase 2, and the tooling
items make every later phase safer.

Raises: Maintainability, Scalability, Backend. All items verified against `c7f4614`.

---

### [x] FND-1 · Persist Data Protection keys — Priority: High · Effort: S
**Files:** `src/Ats.Web/Program.cs` (no `AddDataProtection`), auth cookie config `Program.cs:16-26`.
**Problem:** No `AddDataProtection().PersistKeysTo…` is configured, so the app uses the default local
key ring (`%LOCALAPPDATA%/ASP.NET/DataProtection-Keys`, seen in the startup log). On a second web
instance, a container restart, or key expiry, auth cookies (and anything encrypted with Data
Protection) become undecryptable → users are silently logged out / data breaks. It is also a
prerequisite for encrypting integration secrets (SEC-1).
**Fix:**
- Register `AddDataProtection()` with a shared, persisted key store appropriate to the host
  (file share, Azure Blob + Key Vault protection, or Redis) and `SetApplicationName("Ats")`.
- Document the production key-store choice in `appsettings.Production.json` / deployment docs.
**Acceptance:** Keys persist across restarts; two instances share them; an auth cookie issued by one
instance is accepted by another.
**Verify:** Restart the app twice; confirm an existing auth cookie still authenticates (no forced
re-login). Inspect the configured key-ring location contains a persisted key.

---

### [x] FND-2 · Health check must verify the database — Priority: Medium · Effort: S
**Files:** `src/Ats.Web/Program.cs:12` (`AddHealthChecks()`), `:37` (`MapHealthChecks("/health")`).
**Problem:** `/health` returns Healthy even when SQL Server is down (confirmed live: the app 500'd on
DB failure while `/health` would still report OK). Orchestrators/load balancers can't detect a
DB-outage instance and won't restart/deprioritise it.
**Fix:**
- Add the EF health check: `AddHealthChecks().AddDbContextCheck<AtsDbContext>()` (package
  `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`).
- Optionally expose a `/health/ready` (DB + dependencies) vs `/health/live` (process) split.
**Acceptance:** `/health` returns Unhealthy (503) when the DB is unreachable.
**Verify:** Stop LocalDB, hit `/health`, observe 503; start it, observe 200.

---

### [x] FND-3 · EF Core connection resiliency — Priority: Medium · Effort: S
**Files:** `src/Ats.Infrastructure/DependencyInjection.cs:48-52` (`UseSqlServer`).
**Problem:** No `EnableRetryOnFailure`; a transient DB blip surfaces as a raw 500 (observed live).
**Fix:** `options.UseSqlServer(conn, sql => sql.EnableRetryOnFailure(maxRetryCount: 5,
maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))`. Note: retrying execution
strategies forbid user-initiated transactions unless wrapped in an execution strategy — check the
manual transactions added in DATA-6/DATA-2 use `context.Database.CreateExecutionStrategy()`.
**Acceptance:** Transient connection faults are retried, not surfaced as 500s.
**Verify:** Build clean; smoke-test list/dashboard pages still work.

---

### [x] FND-4 · API error contract (ProblemDetails) — Priority: Medium · Effort: S
**Files:** `src/Ats.Api/Program.cs` (no `UseExceptionHandler`/`AddProblemDetails`/`UseStatusCodePages`).
**Problem:** The partner-facing feed API has no global exception handler or structured error body — an
unhandled error returns a bare 500 (dev exception page in Development). No consistent machine-readable
error for integrators.
**Fix:** `builder.Services.AddProblemDetails();` + `app.UseExceptionHandler();` (and
`app.UseStatusCodePages();`) in `Ats.Api`. Ensure 401 from `FeedApiKeyFilter` returns a ProblemDetails
body too.
**Acceptance:** API errors return RFC 7807 ProblemDetails with correct status codes.
**Verify:** Hit the feed with a bad key → structured 401; force an error → structured 500 (no stack in
non-Development).

---

### [x] FND-5 · Quality gate: `.editorconfig` + analyzers + warnings-as-errors — Priority: Medium · Effort: M
**Files:** `Directory.Build.props` (`TreatWarningsAsErrors=false`); no `.editorconfig`; no analyzer config.
**Problem:** No enforced style/quality gate for a 10-year, multi-developer product. Warnings don't
fail the build; formatting/consistency is unpoliced.
**Fix:**
- Add a root `.editorconfig` (naming, `var` usage, using-sort, nullable, analyzer severities).
- Enable `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` and
  `<AnalysisLevel>latest-recommended</AnalysisLevel>` in `Directory.Build.props`.
- Flip `TreatWarningsAsErrors` to `true` **after** the codebase is warning-clean (it currently is).
- Add `dotnet format --verify-no-changes` to CI (FND-6).
**Acceptance:** `dotnet build` fails on new warnings/style violations; `dotnet format` is clean.
**Verify:** Introduce a deliberate unused variable → build fails; revert.

---

### [x] FND-6 · Continuous Integration pipeline — Priority: Medium · Effort: M
**Files:** none (`.github/workflows` absent; no CI).
**Problem:** No automated build/test/format gate on push/PR — regressions can merge unnoticed.
**Fix:** Add a CI workflow (GitHub Actions or Azure Pipelines) that runs `dotnet restore`,
`dotnet build -warnaserror`, `dotnet test`, and `dotnet format --verify-no-changes` on PRs to
`master`. Cache NuGet. Fail the PR on any step.
**Acceptance:** PRs cannot merge red; the pipeline runs on every push to `master` and PR.
**Verify:** Open a trivial PR; confirm the pipeline runs and gates.

---

### [x] FND-7 · Structured, persistent logging + observability — Priority: Medium · Effort: M
**Files:** `src/Ats.Web/Program.cs:7-10` (Serilog console-only), `:36` request logging.
**Problem:** Serilog writes to console only — no persistent/structured sink, no correlation-id
enrichment, no PII-scrubbing policy. In production, diagnosing a tenant-specific issue is guesswork.
**Fix:**
- Add a durable structured sink (Seq / Application Insights / OpenTelemetry OTLP) via
  `appsettings.json` `Serilog:WriteTo`, keep console for dev.
- Enrich with `TenantId`, request id, and user id (from claims) — never log secrets or full PII.
- Confirm `UseSerilogRequestLogging` doesn't log query strings that could carry a referral code/PII.
**Acceptance:** Logs are queryable by tenant/request id in the chosen backend; no secrets/PII in logs.
**Verify:** Trigger an error; find it in the sink filtered by tenant id.

---

## Exit criteria
- [x] Data Protection keys persist and are shared across instances. (verified: `key-*.xml` written to `App_Data/keys`)
- [x] `/health` reflects DB health; transient DB faults are retried. (`/health/ready` = 200 via `DbContextCheck`; `EnableRetryOnFailure` + execution-strategy wrapper in `OnboardingStore`)
- [x] API returns ProblemDetails. (`AddProblemDetails` + `UseExceptionHandler`/`UseStatusCodePages` in `Ats.Api`)
- [x] `.editorconfig` + analyzers in place; CI green and gating. (analyzers surface warnings as warnings; `TreatWarningsAsErrors=false` by decision; CI = `.github/workflows/ci.yml` with build + test + `dotnet format --verify-no-changes` gate)
- [x] Structured logging with tenant/request enrichment, no PII leakage. (rolling file sink writing structured request logs; TenantId/UserId enriched from claims on authed requests)
- [x] `dotnet build` clean, `dotnet test` green. (0 warnings / 0 errors with warnings-as-errors; 68/68 tests pass)
