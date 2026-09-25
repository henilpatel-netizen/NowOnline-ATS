# Phase 10 · Outbox delivery correctness

Source: `pr-review-toolkit:silent-failure-hunter` trial on the outbox path (25 September 2026), every item
re-verified by the lead against the code at that date. The theme: the worker must never record a delivery
status that is not true, and must never lose or reorder a ReferralTool status update silently.

Contract reference: `docs/integration/referraltool-contract.md` (frozen). It documents the duplicate guard
(line 107) but not the exact response for it, so OUT-3 classifies by status code only.

---

### [x] OUT-1 · Persist the delivery attempt before the status change — Priority: Critical · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/OutboxProcessor.cs:68-84` (single final save), `:108-119`
(`Log` only stages the `WebhookDelivery` row).
**Problem:** ReferralTool accepts the update (2xx) but the final `SaveChangesAsync` fails (SQL outage past the
retries, claim-lease `RowVersion` conflict, shutdown). The staged attempt row is lost with it. After the lease
expires the message is re-sent, ReferralTool's duplicate guard answers 4xx, `hadPriorStatusAttempt` is false,
and the message is marked **Failed** although it was delivered.
**Fix:** save the `WebhookDelivery` row for the status-update call in its own `SaveChangesAsync` immediately
after the HTTP call returns, before the outcome is applied. The check-vacancy log may stay batched.
**Acceptance:** a status update that ReferralTool accepted always leaves a persisted attempt row, even if the
status save that follows fails.
**Verify:** code review of the save order; unit tests cover the classification (OUT-3).

---

### [x] OUT-2 · Isolate per-message failures in the drainer — Priority: High · Effort: S
**Files:** `src/Ats.Worker/OutboxDrainer.cs:36-46`, `src/Ats.Infrastructure/Integration/ReferralToolClient.cs:15,43,57-60`.
**Problem:** any exception from `ProcessAsync` (a stored base URL that no longer parses in `Build`, which runs
outside the client's `try`; a database error) escapes to the cycle-level `catch`. It logs
"Outbox drain cycle failed" with no message or tenant id, `Attempts` never increases so `MaxAttempts` never
dead-letters it, and every other claim in the batch waits out the 300 s lease. This repeats every cycle.
**Fix:** wrap each `ProcessAsync` in its own `try/catch`: log with `claim.Id` and `claim.TenantId`, then record
the failure through the processor's existing defer path (attempt counted, backoff, dead-letter at
`MaxAttempts`) in a fresh scope; break that application's chain and continue with the next group. Move `Build`
inside the client's `try` so a malformed URL becomes an unreached `ReferralCallResult`, not an exception.
Cancellation on shutdown is not a message failure: rethrow `OperationCanceledException` when
`stoppingToken` is cancelled.
**Acceptance:** one failing message cannot stall other applications' messages, is logged with its id and
tenant, and eventually dead-letters.

---

### [x] OUT-3 · Only a duplicate-guard 4xx counts as delivered — Priority: High · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/OutboxProcessor.cs:57-67`.
**Problem:** `idempotentDuplicate = is4xx && hadPriorStatusAttempt` treats every 4xx after an earlier attempt as
"ReferralTool already has it". Attempt 1 times out, attempt 2 gets 401 (key rotated), 403, 408 or 429 (rate
limited): the message is marked **Delivered**, `Success = true`, and the update is lost.
**Fix:** extract the outcome decision into a pure, unit-tested function in `Ats.Application.Integration`
(inputs: reached, HTTP status, had prior attempt; output: Delivered / Transient / Failed). 401, 403, 408 and
429 are **Transient** (retry with backoff) whether or not there was a prior attempt. Other 4xx: Delivered as
an idempotent duplicate after a prior attempt, Failed on a first attempt (unchanged). 5xx and unreached:
Transient (unchanged). The delivery log's `Success` must follow the same decision.
**Acceptance:** auth and rate-limit responses are never recorded as delivered or failed-terminal.

---

### [x] OUT-4 · Enforce per-application ordering in the claim — Priority: High · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/OutboxClaimStore.cs:34-43`, `src/Ats.Worker/OutboxDrainer.cs:32-43`.
**Problem:** when message N is deferred the drainer breaks the chain, but N+1 stays `Processing` with a 300 s
lease. From N's 4th attempt the backoff (480 s+) exceeds the lease, so N+1 is claimed alone and sent first:
ReferralTool can receive "Hired" before "Interview". The integration skill's "N+1 never precedes N" guarantee
does not hold.
**Fix:** in the claim CTE, exclude any row that has an older message for the same `(TenantId, ApplicationId)`
still in `Pending` or `Processing` (`NOT EXISTS`). `Delivered` and `Failed` predecessors do not block (a
terminal failure must not freeze the application forever). Keep the locking hints and the single-statement
atomic claim. This is inside the documented filter-bypass spot; no new bypass.
**Acceptance:** a message is never claimed while an older undelivered message of the same application exists.
**Verify:** manual developer check against SQL Server (the unit suite has no database); document the steps in
the close-out.

---

### [x] OUT-5 · Report an unreadable vacancy-check reply as its own error — Priority: Medium · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/ReferralToolClient.cs:24-31`.
**Problem:** a 2xx with a body that is not valid JSON is swallowed by an empty `catch (JsonException)`. The
message is deferred as "Vacancy not imported yet." for about a day and then fails with the wrong error,
hiding a contract change on ReferralTool's side.
**Fix:** distinguish "reached, 2xx, unparseable" from "exists = false" and defer with an explicit
"Vacancy check returned an unreadable response" error (the response body is already in the delivery log).
**Acceptance:** the error text names the real cause.

---

### [x] OUT-6 · Paused or invalid settings do not burn attempts — Priority: Medium · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/OutboxProcessor.cs:32-39`, `:90-104`.
**Problem:** with the integration disabled, a key cleared, or a stored base URL that fails
`ReferralToolBaseUrl.Validate` (saved before SEC-6), every cycle counts an attempt. After about 22 hours
queued messages go **Failed** permanently, with nothing flagged when the owner fixes the settings.
**Fix:** postpone without `Attempts++` (status back to `Pending`, `NextAttemptAt = now + MaxBackoffSeconds`,
`LastError` set to the reason). Add the base-URL validation to the "settings unusable" check so the client
never sends to an invalid URL.
**Acceptance:** re-enabling a paused integration delivers the queued messages in order.

---

## Task 1 result (OUT-1, 3, 5, 6), done 2026-09-25, awaiting developer review
- `ReferralToolRules` (Application, pure, 77 tests): `ClassifyStatusUpdate`, `MayHaveBeenProcessed`
  (EF expression), `IsDefinitelyNotSent`, `RecordedStatus`, `ConnectionProblem`, `SettingsProblem`,
  `ReadVacancyExists`.
- `WebhookDelivery.HttpStatus` now has three meanings: reply status; `null` = may have been sent, no reply
  (timeout, or the pre-send intent row of a crashed worker); `0` = definitely not sent (connection, DNS, TLS,
  bad URL, cancelled before send). Only null, 2xx and 5xx rows count as "possibly processed".
- The status-update attempt is saved as an intent row **before** the send and updated after, both with
  `CancellationToken.None`; the Delivered save is not cancellable either.
- Settings unusable (disabled, incomplete, URL fails SEC-6 validation) postpone by `MaxBackoffSeconds`
  without counting an attempt. Test connection uses the same completeness/URL rules (ignores the enabled
  switch, so owners can test before enabling), requires a readable reply, and shows the failure reason.
- Known limit (accepted): a crash between saving the intent row and sending leaves a phantom "possibly
  processed" row, so a later real 4xx on that message would be recorded as Delivered.
- Review: 2 fix loops (silent-failure-hunter caught a regression introduced by the OUT-3 fix itself); tenancy PASS, conventions PASS, e2e PASS 39/39; 259/259 unit tests.

## Task 2 result (OUT-2), done 2026-09-25
- `OutboxBatch.RunAsync` (Application, unit-tested) runs the per-application chains; an exception is logged
  with `MessageId`/`TenantId`, recorded via `IOutboxProcessor.RecordFailureAsync` in a fresh scope (reuses
  `DeferAsync`), and stops only that chain. Shutdown cancellation propagates. `HttpIOException` (cut-off body)
  is caught in the client as "may have been sent". `LastError` holds only the exception type.
- Review: 1 round, all PASS.

## Task 3 result (OUT-4), done 2026-09-25, needs the manual SQL Server check below
- Claim CTE: `NOT EXISTS` older `Pending`/`Processing` message of the same `(TenantId, ApplicationId)`,
  read `WITH (READCOMMITTEDLOCK)`. Existing index `(TenantId, ApplicationId, Id)` covers it; no migration.
- Lease fencing (added after review): `OutboxClaim.Lease` from the claim's OUTPUT; `OutboxBatch` skips
  expired-lease claims; the processor only touches a row that is `Processing` with that lease.
- Throughput ceiling: one message per application per poll cycle (burst of k takes ~k x 15 s).
- Review: 1 fix loop (silent-failure-hunter found stale-claim re-sends on a live worker).

### Manual verification (developer, dev database only)
1. Stop `Ats.Worker`. For a tenant with the integration enabled, move one application that has a
   `SourceCode` through two new stages: two Pending `OutboxMessages` rows, N (lower Id) and N+1.
2. Set N to `Attempts = 4`, `NextAttemptAt` = UTC now + 600 s. Run `dotnet run --project src/Ats.Worker`
   for a few cycles. Expected: N+1 stays Pending, `NextAttemptAt` unchanged, no `WebhookDeliveries` for
   N+1, and no "Outbox drain cycle failed" in the log (that would mean the `Lease` column did not map).
3. Set N's `NextAttemptAt` to now. Expected: N delivered in one cycle, N+1 in the next; N's delivery rows
   first.
4. Fresh pair, set N to `Status = 2` (Failed). Expected: N+1 claimed on the next cycle.
5. Cross-instance: SSMS session 1 `BEGIN TRAN; UPDATE OutboxMessages SET Status = 3 WHERE Id = <N>;`
   (no commit); session 2 runs the claim statement with literal values. Expected: session 2 waits; after
   `COMMIT` it returns without N+1.
6. Fencing: set `Integration:ClaimLeaseSeconds` to 20, point the base URL at a listener that never replies,
   queue messages for three applications, start the worker, let the lease expire while it hangs on claim 1,
   restore the URL and start a second instance. Expected: the second instance delivers claims 2 and 3; the
   first does not send them when its call times out; its claim-1 save fails on RowVersion (worker log).

### [x] OUT-7 · Surface a blocked integration in the health signals — Priority: High · Effort: S
**Files:** `src/Ats.Infrastructure/Shell/ShellSummaryService.cs:37`, `src/Ats.Infrastructure/Dashboard/DashboardService.cs:88-100`,
`src/Ats.Application/Shell/ShellSummary.cs:14`, `src/Ats.Application/Dashboard/DashboardSummary.cs` (`IntegrationHealth`).
**Problem:** the sidebar alert (`IntegrationUnhealthy`) and dashboard integration health count only `Failed`
messages. Since OUT-6 and the final-review cleanup, an enabled integration with unusable settings (incomplete,
invalid base URL) or rejected credentials (401/403) postpones forever and never produces `Failed`, so the owner
gets no alert. (Before, messages dead-lettered after ~22 h and the alert fired.)
**Fix:** the integration is **blocked** when it is enabled and either `ReferralToolRules.ConnectionProblem`
returns a reason, or the most recent `WebhookDelivery` attempt got 401/403
(`ReferralToolRules.IsCredentialRejection`). Blocked lights the same sidebar alert and shows as unhealthy on the
dashboard, with the reason. A deliberately disabled integration stays silent. Put the "blocked?" decision in a
pure Application rule and unit-test it; keep the shell summary to one cheap query batch (it runs on every page).
**Acceptance:** wrong keys or broken settings on an enabled integration light the alert within one worker
cycle; disabling the integration does not.
**Decision:** developer chose this over reverting to dead-lettering (2026-09-25).
**Done (2026-09-25, awaiting developer review):** `ReferralToolRules.BlockedReason` (pure, 12 tests) drives
`ShellSummary.IntegrationBlocked` (sidebar alert + bell) and a danger attention item on the dashboard.
"Latest attempt" skips null-status intent rows. Saving enabled settings wakes the tenant's postponed messages
(best effort, fully swallowed and logged, rows detached so the audit write is unaffected). New index
`IX_WebhookDeliveries_TenantId_Id`, migration `AddWebhookDeliveryTenantLatestIndex` **not applied** (manual).
Final-review cleanup in the same pass: 401/403 postpone (no attempt counted), `OutboxBatch` flattened
(ordering is the claim's job), consistent post-send cancellation tokens.

## Not fixed (uncertain, noted)
- Claims orphaned if the connection drops after the claim statement commits but before its OUTPUT rows are
  read: they wait out the lease (300 s) with nothing logged. Pre-existing, rare.
- A non-transport exception thrown after the pre-send intent row is saved leaves a `null`-status row that
  counts as "possibly processed" (same class as the accepted crash window in Task 1).
- Shutdown cancellation is logged as a network failure and then an error; OUT-2's cancellation rule covers
  the drainer side.
- `OutboxOutcome.Skip` is returned without a log line (`OutboxProcessor.cs:29-30`).
- A retried claim statement (execution strategy) can leave rows `Processing` until the lease expires: a delay,
  not a loss.

## Execution (`/ats-ship`)
Three tasks, in this order, one implementer at a time:
1. **Task 1: processor outcome correctness:** OUT-3, OUT-1, OUT-6, OUT-5 (`OutboxProcessor`, client, new pure
   classifier + tests).
2. **Task 2: drainer isolation:** OUT-2.
3. **Task 3: claim ordering:** OUT-4.

## Exit criteria
- [x] OUT-1..6 ticked; `dotnet build` 0 warnings, `dotnet test` green, `dotnet format` clean.
- [x] tenancy-guard, conventions and silent-failure-hunter PASS on each task.
- [x] Integration skill updated (delivery-status rules, ordering guarantee, attempt accounting).
