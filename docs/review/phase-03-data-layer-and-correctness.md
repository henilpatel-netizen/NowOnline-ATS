# Phase 3 — Data Layer & Correctness

Database indexing, transactional integrity, integration idempotency, and a few correctness bugs
(timezone, a KPI, feed bounds). These are the changes that keep data right and queries fast as
volume grows. One additive migration covers the new indexes + concurrency tokens.

Raises: Backend, Scalability. Verified against `c7f4614`.

> **Status (2026-07-31):** All eight items implemented. Migration
> `DataLayerIndexesAndConcurrency` (5 indexes + `RowVersion` on `PipelineTemplates`/`TenantSettings`)
> must be applied manually before the app runs (the new mapped columns). DATA-4 uses a browser-timezone
> `<local-time>` tag helper (per-user-correct, no schema change). DATA-2 dedupes on our side (the frozen
> contract has no idempotency-key field). The atomic outbox claim was verified running live against
> LocalDB.
>
> **Live smoke test (2026-08-27, after the migration was applied):** all items verified against the
> running app. Three DATA-4 follow-ups were found and fixed during testing:
> (1) `<local-time>` must be written with an explicit end tag — a self-closing tag-helper element makes
> Razor swallow the markup that follows it (it had silently dropped "· N attempt(s)" from the delivery
> log and the ":" from the dashboard eyebrow); the helper now forces `TagMode.StartTagAndEndTag`.
> (2) the drawer's stage history was still formatted server-local in `ApplicationCardQuery`, now carried
> as a UTC instant and rendered per viewer. (3) the dashboard "today" eyebrow used `DateTimeOffset.Now`;
> it now renders in the viewer's timezone via a new `weekday` format. Rendering assembles date parts
> explicitly so the house day-first, 24-hour format is preserved on every machine — only the ZONE
> follows the viewer, never the format.

---

### [x] DATA-1 · Add indexes for the hot query paths — Priority: High · Effort: S
**Files:** `src/Ats.Infrastructure/Persistence/Configurations/JobApplicationConfiguration.cs:15`
(only unique `(TenantId,JobId,CandidateId)`), `CandidateConfiguration.cs:17` (only `(TenantId,Email)`),
`ApplicationEventConfiguration.cs:12` (`(TenantId,ApplicationId,OccurredAt)`).
**Problem:** No index supports the most common predicates → full scans/sorts on the largest tables:
- `Applications.Status` filters (dashboard, shell, list); `ids.Contains(CandidateId)` in
  `CandidateListQuery.cs:36` (CandidateId is the 3rd composite column → unusable).
- `Candidates` default sort `LastName, FirstName` (`CandidateListQuery.cs:28`) unindexed.
- Dashboard time-to-hire / idle filter `ApplicationEvents` on `ToStageId`+`OccurredAt` /
  `FromStageId`+`OccurredAt` (`DashboardService.cs:39-53`) unindexed.
**Fix (one migration):** add `Applications(TenantId, Status)`, `Applications(TenantId, CandidateId)`,
`Candidates(TenantId, LastName, FirstName)`, `ApplicationEvents(TenantId, OccurredAt)`
(consider `(TenantId, ToStageId, OccurredAt)`). Create the migration file; **developer applies it**.
**Acceptance:** Execution plans seek instead of scan on list/dashboard/board queries.
**Verify:** Create migration, review it contains only `CreateIndex`; after manual apply, the app runs;
optionally check an actual plan for a seek.

---

### [x] DATA-2 · Outbox delivery idempotency + correct duplicate handling — Priority: High · Effort: M
**Files:** `src/Ats.Infrastructure/Integration/OutboxProcessor.cs:50-69`,
`.../ReferralToolClient.cs`, dashboard failure tile `DashboardService.cs:76-78`.
**Problem:** At-least-once delivery with no idempotency key. A lost response after ReferralTool
processed the update → resend → ReferralTool returns a "duplicate" 4xx → recorded as **Failed**,
inflating the "status updates failed to deliver" tile.
**Fix:** Send a stable idempotency key (outbox message Id, or `ApplicationId+ToStageId`) on the status
update so ReferralTool can dedupe; treat the documented "duplicate" 4xx as `Delivered`, not `Failed`.
Confirm against the frozen contract in `docs/integration/referraltool-contract.md` (do not change the
contract — add the key only if the contract permits, else dedupe server-side by tracking last-sent
status per application).
**Acceptance:** Re-delivery does not create duplicates or false failures.
**Verify:** Simulate a timeout-then-retry; the second attempt resolves to Delivered, tile unaffected.

---

### [x] DATA-3 · Atomic outbox claim (scale-out safety) — Priority: Medium · Effort: M
**Files:** `src/Ats.Infrastructure/Integration/OutboxClaimStore.cs:13-19`,
`OutboxMessageConfiguration.cs:18`.
**Problem:** `ClaimDueAsync` selects `Pending && NextAttemptAt<=now` but never transitions state or
locks — safe only because one worker runs; two workers would double-deliver. Also the only index
leads with `TenantId`, unused by this predicate → cross-tenant scan each poll.
**Fix:** Claim atomically (`UPDATE TOP(n) … SET Status=Processing OUTPUT inserted.*` or
`FromSqlRaw` with `WITH (UPDLOCK, READPAST, ROWLOCK)`); add index `(Status, NextAttemptAt)` (no tenant
lead). Add a `Processing` state + a stuck-message reclaim (e.g. Processing older than X → Pending).
**Acceptance:** Two worker instances never deliver the same message; claim uses a seek.
**Verify:** Run two drainer instances against seeded pending rows; no duplicate `WebhookDelivery`.

---

### [x] DATA-4 · Store and display times in UTC with per-user timezone — Priority: Medium · Effort: M
**Files:** views call `.ToLocalTime()` → renders the **server's** local time:
`Views/Audit/Index.cshtml`, `Views/Integration/_DeliveryRows.cshtml`, `Views/Jobs/Index.cshtml`,
`Views/Shared/Partials/_CandidateDrawer.cshtml`. Data is stored `DateTimeOffset.UtcNow` (good).
**Problem:** A recruiter in another timezone (or any non-server-TZ deployment) sees wrong times on
applied-dates, audit timestamps, delivery attempts, and "days in stage". Incorrect for a global
multi-tenant product.
**Fix:** Keep UTC in the DB; convert for display to the **user's** timezone (a per-user/tenant
`TimeZoneId` preference, or the browser's zone). Centralise via a small `IClock`/display helper or a
tag helper; remove `ToLocalTime()` from views. Relative-time helpers already take an explicit `now` —
pass UTC consistently.
**Acceptance:** Timestamps render in the viewing user's timezone regardless of server TZ.
**Verify:** Change the server/user TZ; confirm a known applied-at renders correctly for the user.

---

### [x] DATA-5 · Correct the offer-acceptance KPI — Priority: Medium · Effort: S
**Files:** `src/Ats.Infrastructure/Dashboard/DashboardService.cs:38-55`.
**Problem:** Numerator `hires = hireSpans.Count` counts *events* into any Hired-outcome stage, not
distinct applications; re-entry or multiple hired stages over-count. `Math.Max(hires, progressed)`
hides an >100% ratio but the numerator is still inflated.
**Fix:** Count **distinct `ApplicationId`** for both numerator and denominator; keep the documented
denominator caveat (proxy = applications that progressed in the window) or refine to true
offer-stage-reached.
**Acceptance:** The KPI is a defensible ≤100% ratio.
**Verify:** Unit-test the calc with a hired-stage re-entry scenario (ties to QUAL tests).

---

### [x] DATA-6 · Atomic application-create — Priority: Medium · Effort: S
**Files:** `src/Ats.Application/Applications/ApplicationService.cs:92-104` (two `SaveChanges`:
row, then event+outbox). Note `MoveStageAsync:126-138` is already atomic — good.
**Problem:** A crash between the two saves leaves an application with no initial `ApplicationEvent`
and no outbox message → orphan + silent under-delivery to ReferralTool.
**Fix:** Commit the application + initial event + outbox message in a single unit of work
(one transaction via the execution strategy from FND-3, or rely on EF fixup to save all in one
`SaveChanges`). Apply the same review to the careers apply path (`CareerService.ApplyAsync`).
**Acceptance:** Create is all-or-nothing.
**Verify:** Code review + a test that a failure before commit leaves no partial rows.

---

### [x] DATA-7 · `AsNoTracking` on entity-returning reads — Priority: Low · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/DeliveryLogService.cs:20-26`,
`.../Persistence/Repositories/VacancyFeedRepository.cs:17-22`. (Projection query services are already
untracked — good.)
**Problem:** These return tracked entities on pure reads → needless change-tracker overhead.
**Fix:** Add `.AsNoTracking()` to both (or set `QueryTrackingBehavior.NoTracking` as the context
default and opt back in for the write repositories).
**Acceptance:** Read paths don't populate the change tracker.
**Verify:** Build clean; delivery log + feed still render/return correctly.

---

### [x] DATA-8 · Optimistic concurrency on pipeline & tenant-settings edits — Priority: Low · Effort: M
**Files:** `RowVersion` exists only on `JobApplication` and `OutboxMessage`
(`Configurations/JobApplicationConfiguration.cs`, `OutboxMessageConfiguration.cs`). Pipeline template
edit (`PipelineTemplateService.SaveAsync`) and integration settings (`IntegrationSettingsService`)
have none.
**Problem:** Two admins editing the same pipeline or settings silently clobber each other
(last-writer-wins) — realistic once tenants have multiple Owners (needs FEAT-2).
**Fix:** Add a `RowVersion` to `PipelineTemplate` and `TenantSettings`; surface a friendly
"changed by someone else, reload" on conflict (mirror the board's pattern). One additive migration.
**Acceptance:** Concurrent edits produce a conflict message, not a silent overwrite.
**Verify:** Two tabs edit the same pipeline; the second save reports a conflict.

---

## Exit criteria
- [x] Indexes migration created (apply manually); hot paths seek. (5 `CreateIndex` in `DataLayerIndexesAndConcurrency`)
- [x] Outbox is idempotent; duplicates aren't false failures; claim is atomic + seekable. (atomic claim verified live; duplicate-guard 4xx after a prior attempt -> Delivered; `IX_OutboxMessages_Status_NextAttemptAt`)
- [x] Times render in the user's timezone; offer-acceptance KPI is correct. (`<local-time>` tag helper + site.js; distinct-application numerator with re-entry test)
- [x] Application-create is atomic; pure reads untracked; pipeline/settings edits are concurrency-safe. (transaction via execution strategy; `AsNoTracking`; `RowVersion` round-trip with conflict message)
- [x] `dotnet build` clean, `dotnet test` green. (0/0 warnings; 75/75 tests)
