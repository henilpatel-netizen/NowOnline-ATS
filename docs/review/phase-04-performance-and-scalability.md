# Phase 4 — Performance & Scalability

The app is correct but pays a per-page query tax and lacks caching; at hundreds of tenants this is
the first thing that falls over. These items add a caching layer and make the heaviest queries
bounded and server-side. (Do DATA-1 indexes first — caching + indexes compound.)

Raises: Performance, Scalability. Verified against `c7f4614`.

---

### [ ] PERF-1 · Cache the app-shell summary + branding per tenant — Priority: High · Effort: M
**Files:** `src/Ats.Infrastructure/Shell/ShellSummaryService.cs:35-49`,
`src/Ats.Infrastructure/Branding/TenantBrandingService.cs:31-37`; consumed by `<vc:branding>`,
`<vc:sidebar-nav>`, `<vc:top-bar>` in `_Layout.cshtml` on **every** authenticated page.
**Problem:** ~7 DB queries per page (branding 2 + shell summary 5), including a **correlated
`MAX(OccurredAt)` subquery per active application** to compute the idle count
(`ShellSummaryService.cs:41-46`). Correctly deduped per-request (both services are Scoped with a
`_cached` field), but it re-runs on every navigation.
**Fix:** Register `IMemoryCache`/`HybridCache`; cache `TenantBranding` (TTL 5–10 min) and
`ShellSummary` (TTL 30–60 s) keyed by tenant id. Invalidate branding on save (the service already
nulls its per-request cache at `:67` — extend to evict the shared cache).
**Acceptance:** Repeat navigations hit cache, not the DB, for shell + branding.
**Verify:** Log SQL for two back-to-back page loads; the shell/branding queries appear once, not twice.

---

### [ ] PERF-2 · Cache the dashboard + collapse its queries — Priority: High · Effort: M
**Files:** `src/Ats.Infrastructure/Dashboard/DashboardService.cs:20-97` (~15 sequential queries;
`GroupBy(Origin)` and by-stage scan all applications; 4 separate outbox-status counts; repeats the
shell idle subquery).
**Problem:** The dashboard is the heaviest authenticated page and recomputes everything per load.
**Fix:** Cache the whole `DashboardSummary` per tenant (TTL 30–60 s) — it's a glanceable overview, not
real-time. Collapse the 4 outbox counts into one `GroupBy(Status)`. Reuse the cached shell idle count
(PERF-1). Ensure the date-bounded queries use the DATA-1 `ApplicationEvents` index.
**Acceptance:** Dashboard load issues a handful of queries on a miss and ~0 on a hit within TTL.
**Verify:** SQL log shows one grouped outbox query; second load within TTL hits cache.

---

### [ ] PERF-3 · Server-side aggregation in list projections — Priority: Medium · Effort: M
**Files:** `src/Ats.Infrastructure/Jobs/JobListQuery.cs:42-73` (loads every active application + every
applicant name for the visible jobs, then `GroupBy`/`Take(3)` in memory),
`CandidateListQuery.cs:33-64`.
**Problem:** Cost scales with total applicants, not what's displayed — a job with thousands of
applicants loads them all to render three avatars and stage tallies.
**Fix:** Do stage-count `GroupBy` in SQL; fetch top-3 applicant names per job server-side (correlated
`Take(3)` or window function). `totalByJob` already shows the correct server-side pattern — mirror it.
**Acceptance:** Per-page cost is flat regardless of applicant volume per job.
**Verify:** Seed a job with many applications; the jobs list query count/rows don't grow with it.

---

### [ ] PERF-4 · Scalable search (drop leading wildcard) — Priority: Medium · Effort: M
**Files:** `src/Ats.Infrastructure/Search/GlobalSearchService.cs:23-50`; same pattern in
`JobListQuery.cs:23`, `CandidateListQuery.cs:21-23`, `AuditQuery.cs:23-25`.
**Problem:** `LIKE '%term%'` (leading wildcard) is non-sargable → full scans on Jobs/Candidates/
Applications per search. Escaping/parameterisation are correct (no injection).
**Fix:** For typeahead, prefer prefix match (`term%`, sargable) where UX allows; for true substring/
relevance at scale, adopt SQL Server full-text search (`CONTAINS`) on Title/ExternalRef/name/email.
Keep the existing debounce + `MinTermLength`.
**Acceptance:** Search uses an index seek / full-text index rather than three table scans.
**Verify:** Plan shows a seek/full-text scan, not a clustered-index scan, at volume.

---

### [ ] PERF-5 · DbContext pooling + fewer worker round-trips — Priority: Medium · Effort: M
**Files:** `src/Ats.Infrastructure/DependencyInjection.cs:48-52` (`AddDbContext`, not pooled);
`OutboxProcessor.cs:88-100` (per-message `SaveChanges` in `LogAsync`, plus status saves).
**Problem:** Per-scope DbContext allocation under load; each outbox message incurs 3+ DB round-trips.
**Fix:** Switch to `AddDbContextPool<AtsDbContext>` (the registered interceptor is pool-compatible).
In the worker, batch the `WebhookDelivery` insert with the status update in a single `SaveChanges`.
**Acceptance:** Lower allocation under load; fewer round-trips per delivered message.
**Verify:** SQL log shows one save per message instead of three; load test shows reduced GC/alloc.

---

### [ ] PERF-6 · Immutable caching for all static assets — Priority: Medium · Effort: S
**Files:** `_Layout.cshtml:13-18,52-55`; `Program.cs:57,60,65` (`MapStaticAssets`/`.WithStaticAssets`).
Confirmed live: vendor CSS serves `Cache-Control: no-cache` and re-downloads on every navigation.
**Problem:** Vendor libs (bootstrap/jQuery/htmx) aren't fingerprinted; CSS/fonts are revalidated each
navigation — wasted requests and part of the perceived blink.
**Fix:** Route all wwwroot assets through `MapStaticAssets` fingerprinting (drop hand-rolled
`asp-append-version` where MapStaticAssets already fingerprints); confirm the subset icon woff2 and
fonts are served with a hashed path + `Cache-Control: immutable`. Verify prod config (dev uses
`no-cache` deliberately).
**Acceptance:** Repeat loads serve assets from cache (`200 (from cache)` / `304`-free immutable).
**Verify:** In prod-like config, second navigation makes no CSS/font network requests.
**Note:** This compounds with NAV-1 (boosted navigation) in Phase 5 to remove the blink.

---

## Exit criteria
- [ ] Shell, branding, and dashboard are cached per tenant with sane TTLs + invalidation.
- [ ] List projections aggregate server-side; search is sargable/full-text.
- [ ] DbContext pooled; worker round-trips reduced.
- [ ] Static assets immutable-cached in production.
- [ ] `dotnet build` clean, `dotnet test` green; a repeat page load issues minimal SQL.
