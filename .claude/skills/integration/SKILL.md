---
name: integration
description: The Ats - ReferralTool integration - vacancy push, outbox enqueue, the worker delivery loop, the ReferralTool client, and integration settings. Read before changing any integration behaviour.
---

# Ats - ReferralTool Integration (Phase 3)

## Vacancy push
Ats pushes vacancies to ReferralTool's REST vacancy API (contract Appendix D); there is no pull feed
and no separate API host any more.
- **Triggers:** `JobService` calls `IOutboxEnqueuer.StageVacancySyncAsync(job)` before `SaveChanges`,
  so the message commits with the job change. Publish and Close always stage; Update and Delete stage
  only when `PublishedAt` is not null (a job that was never published is unknown to ReferralTool).
- **Snapshot:** `OutboxEnqueuer.StageVacancySyncAsync` no-ops only when there is no
  `ReferralToolCustomerId`; it stages **even while the integration is paused** (unlike the candidate
  path), because a job deleted while paused could never be re-queued ("Sync vacancies now" skips
  soft-deleted jobs). The worker postpones paused messages without counting attempts (`SettingsProblem`)
  and a settings save pulls them forward. It stores `OutboxMessage { Kind = VacancySync, JobId, ExternalVacancyId =
  ExternalRef, Payload }`, where `Payload` is a JSON `VacancyPayload` (Application, pure, unit-tested:
  `From` maps the job, `TryParse` reads it back). The worker sends the snapshot as-is and never
  re-reads the job. Mapping: `Id`=ExternalRef, `Title` first 150 chars, `Url`=
  `{CareerSiteBaseUrl}/careers/{slug}/jobs/{ExternalRef}`, `Location`=City ?? Name, `EmploymentType`
  enum name, `Categories`=`[Department.Name]` or `[]`, `Inactive`=`IsDeleted || Status != Published`.
- **`Integration:CareerSiteBaseUrl`** (`IntegrationOptions`) is bound and validated on start in
  `Ats.Web` (`Program.cs`, absolute http(s) URL, fails start-up otherwise). Development uses
  `https://localhost:7044`; every other environment must set it (e.g. env var
  `Integration__CareerSiteBaseUrl`). One host per environment.
- **Per-job ordering:** the claim holds a `VacancySync` back while an older message for the same
  `(TenantId, JobId)` is `Pending`/`Processing` (see Ordering below), served by the filtered index
  `(TenantId, JobId, Id) WHERE JobId IS NOT NULL`. Migration `20260928133755_AddVacancySyncOutbox`
  (its `Down` deletes `VacancySync` rows and their deliveries; "Sync vacancies now" regenerates them).
- **Call decision:** after `checkvacancyexists`, `ReferralToolRules.DecideVacancyCall(exists, inactive)`:
  exists -> `PUT /v1.0/vacancy/{id}` (full replace; closed/deleted jobs go as `Inactive = true`), not
  exists and active -> `POST /v1.0/vacancy`, not exists and inactive -> nothing (Delivered). Ats
  **never** calls `DELETE`: ReferralTool renames a deleted vacancy's ExternalId, which orphans referred
  candidates and breaks later status updates.
- **`ClassifyVacancyCall`:** 2xx -> Delivered; unreached / 5xx -> Transient; 401 / 403 -> Postpone;
  400 whose `errors` object has entries (`HasValidationErrors`) -> Failed (terminal); any other status
  -> Transient (the body is kept in `LastError`). ReferralTool answers every vacancy failure with 400, so
  "Id is not unique" / "Vacancy does not exist" retry and re-read existence.
- **No pre-send intent row** for vacancy calls: existence is re-checked on every attempt, so a POST
  that timed out after ReferralTool stored it becomes a PUT next time.
- **Sync vacancies now** (Integrations screen, POST `SyncVacancies`): `QueueVacancySyncAsync` returns
  null when `SettingsProblem` says the settings are unusable (nothing queued), else stages a sync for
  every non-draft job and returns the count. Audited as `VacancySyncQueued`.
- **Known limitation:** `Job` has no concurrency token, so two overlapping edits of one job can leave
  the older snapshot as the last one sent. "Sync vacancies now" repairs it.
- **Known limitation:** renaming a department or location does not re-sync the affected jobs; the new
  name reaches ReferralTool with the next change to each job or "Sync vacancies now".
- **Known limitation:** `checkvacancyexists` uses the configured `ReferralToolCustomerId`, while POST/PUT
  use the customer behind the `X-Api-Key`. A key paired with the wrong customer id sees "not exists",
  POSTs, gets "Id is not unique" (Transient) and loops until dead-letter. Test connection cannot detect
  this, so operators must copy the key and the id from the same ReferralTool customer.
- The two old feed columns on `TenantSettings` (`FeedApiKeyHash` and the last-pull timestamp) are
  unused and kept one release for rollback; a follow-up migration drops them. Do not read or write them.

## Outbox enqueue (candidate status)
`IOutboxEnqueuer.StageAsync` adds an `OutboxMessage` (payload snapshot) in the same unit of work as the
stage `ApplicationEvent`, only on first arrival at a stage, when the application has a `SourceCode` and
`TenantSettings.IntegrationEnabled` with a `ReferralToolCustomerId`. Wired into
`ApplicationService.MoveStageAsync`/`CreateApplicationAsync` and `CareerService.ApplyAsync`. Mapping:
`Code`=SourceCode, `ExternalVacancyId`=Job.ExternalRef, `ExternalCandidateId`=Candidate.Key,
`CandidateStatus`=stage.ReferralStatusOverride ?? stage.Name. `Kind` = `CandidateStatus`; vacancy
messages leave `ApplicationId` null.
It also skips when an identical `CandidateStatus` message (same `Code`, `ExternalVacancyId`,
`ExternalCandidateId` and `CandidateStatus`, `ReferralToolRules.SameCandidateStatus`) exists that
ReferralTool has or may still get (`ReferralToolRules.BlocksIdenticalResend`): it is `Pending`,
`Processing` or `Delivered`, or it is `Failed` with a `StatusUpdate` delivery that passes
`MayHaveBeenProcessed` (`PossiblyProcessedStatusUpdates`, the same definition the worker uses). A
`Failed` message ReferralTool never received (dead-lettered on "Vacancy not in ReferralTool yet.",
only `HttpStatus` 0 attempts, a 4xx before the event type was seeded) does not block, so the status is
queued again. The contract forbids resending an identical stage, and a candidate removed from a job and
added again is a new application with no events. A skip is logged at Information with the application
id, the matched message id and status, and the `CandidateStatus`, never `Code` or the candidate key.
All three rule parts are pure and unit-tested; the composed queries were checked with `ToQueryString()`
(fully translated, tenant filter applied), but no e2e covers them because they only run for referral
applications.
- **Known limitation:** a deleted and re-created candidate gets a new `Candidate.Key`, so ReferralTool
  sees a new candidate.
- **Known limitation:** claim ordering is per `(TenantId, ApplicationId)`. A re-applied candidate's new
  application can send a later stage before the old application's still-pending earlier stage, which
  breaks the contract's per-candidate ordering. The proper fix is a third claim `NOT EXISTS` on
  `(TenantId, ExternalCandidateId, ExternalVacancyId)` plus an index; out of scope for now.
- **Known limitation:** a Pending or Processing identical message blocks the resend on the assumption
  that it will be delivered. If it later dead-letters without ReferralTool receiving it, the re-applied
  application's status is never queued (first-arrival rule); the only trace is the Information log of the
  skip. The same `(TenantId, ExternalCandidateId, ExternalVacancyId)` index would also make the dedupe
  lookup cheap; today it scans the tenant's outbox.

## Worker delivery (Ats.Worker)
`OutboxDrainer` polls every `Integration:PollSeconds`. `OutboxClaimStore` claims due Pending messages
of both kinds across all tenants (raw SQL, see below); per message the `OutboxProcessor` sets
`WorkerTenantContext.CurrentTenantId`, checks the settings, fails a `VacancySync` with an unreadable
payload at once, pre-checks the vacancy (`checkvacancyexists`), then either runs the vacancy call
(above) or posts `candidatestatusupdate`. `IReferralToolClient` sends every call through one
`SendAsync(settings, method, path, payload)` to `{base}/v1.0/{path}` with `X-Api-Key` + `X-Auth-Token`.
Every attempt logs a `WebhookDelivery` (`CheckVacancy`, `StatusUpdate`, `VacancyCreate`,
`VacancyUpdate`). The outcome rules live in `ReferralToolRules` (Application, pure, unit-tested;
change them there, not in the processor):
- `ClassifyStatusUpdate`: unreached / 5xx / 408 / 429 -> Transient (backoff, dead-letter after
  `MaxAttempts`); 401 / 403 (`IsCredentialRejection`, also applied to the vacancy check) -> Postpone
  (like `SettingsProblem`: `MaxBackoffSeconds`, no attempt counted, never dead-letters); 2xx -> Delivered; other 4xx -> Delivered when an earlier attempt **may have been
  processed** (ReferralTool's duplicate guard rejecting a re-send), else Failed (terminal).
- `WebhookDelivery.HttpStatus` meanings: reply status; `null` = may have been sent, no reply (timeout, cut-off
  body, or a pre-send intent row left by a crashed worker); `0` = definitely not sent (connection, DNS, TLS,
  bad URL, cancelled before send). `MayHaveBeenProcessed` (EF expression) = null, 2xx or 5xx.
- The status-update attempt is saved as an intent row **before** the send, then updated with the result,
  both with `CancellationToken.None`, before the message status changes (OUT-1).
- `SettingsProblem` (disabled, incomplete, base URL fails `ReferralToolBaseUrl.Validate`) postpones by
  `MaxBackoffSeconds` **without** counting an attempt, so a paused integration keeps its queue (OUT-6).
  Test connection uses `ConnectionProblem` (same rules minus the enabled switch).
- "Latest attempt" for the health signals skips `HttpStatus` null rows, so a pre-send intent row (in
  flight, or left by a crashed worker) cannot hide a 401/403; `0` rows count as a real outcome.
- A status update whose vacancy is not in ReferralTool defers with "Vacancy not in ReferralTool yet."; a
  2xx vacancy check with an unreadable body defers with its own error instead (OUT-5).

Per message, `OutboxBatch.RunAsync` (Application, unit-tested) isolates failures: an exception is logged
with `MessageId`/`TenantId`, recorded through `IOutboxProcessor.RecordFailureAsync` in a fresh scope (same
defer rules; `LastError` holds only the exception type, the detail stays in the worker log), and the
other claims carry on. There are no per-application or per-job chains: the claim already returns at most
one message per application and per job, so ordering is enforced there. Shutdown cancellation propagates
and is never counted (OUT-2).

**Multi-instance safe (Phase 3, DATA-3).** `OutboxClaimStore` claims due messages atomically in one
statement (`UPDATE ... WITH (READPAST, UPDLOCK, ROWLOCK) ... SET Status = Processing OUTPUT inserted.*`),
so two worker instances can never claim the same row. A claim sets `NextAttemptAt` to a visibility-timeout
lease (`Integration:ClaimLeaseSeconds`, default 300s); a crashed worker's `Processing` messages are
reclaimed once the lease elapses. `OutboxMessage.RowVersion` remains a concurrency token.

**Ordering + fencing (phase 10, OUT-4).** The claim skips any message with an older `Pending`/`Processing`
message for the same `(TenantId, ApplicationId)` or the same `(TenantId, JobId)` (two `NOT EXISTS ... WITH
(READCOMMITTEDLOCK)`, one per key so each uses its index, `ORDER BY m.Id`; a NULL key never matches, so each
kind only blocks its own stream). The hint must block on locked rows and must not read RCSI versions;
Delivered/Failed predecessors do not block. Cost: one message per application or job per poll cycle. Each
`OutboxClaim` carries its `Lease` (`OUTPUT inserted.NextAttemptAt
AS Lease`) as a fencing token: `OutboxBatch` skips claims whose lease has expired, and the processor only
touches a row that is `Processing` with that exact lease (`OutboxClaim.IsOwnedBy`). Remaining window: a
lease expiring during one in-flight call can cause a duplicate send, which ReferralTool's duplicate guard
absorbs. Keep `ClaimLeaseSeconds` above a typical batch's run time. Manual SQL Server verification steps:
`docs/review/phase-10-outbox-delivery-correctness.md`.

## ReferralTool-side setup (required before the loop runs; data/config only, no RT code change)
The integration uses only existing ReferralTool endpoints. The developer configures ReferralTool and
fills the ATS integration settings to match:
1. For every customer served by Ats, set ReferralTool `ImportSettings.Enabled = 0`, so its importer no
   longer imports or deactivates that customer's vacancies. No `ImportSetting` row pointing at Ats is
   needed any more.
2. Copy ReferralTool config `Kafka:AuthToken` into the ATS `ReferralToolAuthToken` (X-Auth-Token).
3. Copy a ReferralTool `ApiKeys` row GUID for the customer into the ATS `ReferralToolApiKey`
   (X-Api-Key). ReferralTool parses `X-Api-Key` as a GUID; a non-GUID 401s and the queue is postponed
   until the key is fixed. Neither credential expires.
4. Seed a ReferralTool `CustomerEventType` for every pipeline stage that should award points, matching
   the stage's `ReferralStatusOverride ?? Name` (case-insensitive). Unmatched statuses 400 and never
   accrue points; prefer pinning `ReferralStatusOverride` to the exact ReferralTool event-type strings.
5. Set `Integration:CareerSiteBaseUrl` for the environment's `Ats.Web` (see Vacancy push).
Known accepted risk (ReferralTool unchanged): the Kafka endpoints trust `CustomerId` from the body and
`X-Auth-Token` is one global value, so keep both secrets restricted to operators.
Operational note: a stale/invalid `?ref=` code makes the first status update 400 and the message goes
`Failed` permanently (the candidate is never created in ReferralTool). Monitor `Failed` in the
Integration delivery log (filter by state).

## Settings and log (back-office, Owner-only)
`IntegrationController` edits `TenantSettings` integration fields and queues a full vacancy sync
(`SyncVacancies`). `Deliveries` shows recent `OutboxMessage`s with their `WebhookDelivery`
attempts. After a successful save with the integration enabled, `UpdateAsync` pulls `NextAttemptAt`
forward to now on this tenant's postponed `Pending` messages (tracked update, never `Processing` rows, a
concurrency conflict is ignored), so a fix is retried on the next poll instead of after `MaxBackoffSeconds`.
Test connection runs `checkvacancyexists` for the `ExternalRef` of the tenant's first non-draft job.

Local testing: `Integration:AllowInsecureReferralToolUrl` (`IntegrationOptions`, set to `true` only in the
Web and Worker `appsettings.Development.json`) lets the base URL be `http://` and localhost/private
addresses, for a ReferralTool API on this machine (`https://localhost:44377` via IIS Express, or
`http://localhost:47882`). It is threaded into `ReferralToolBaseUrl.Validate` and `ConnectionProblem` /
`SettingsProblem` / `BlockedReason`, so the settings save, Test connection, the worker and the blocked
alert all agree. Both hosts refuse to start when it is on outside Development (`ValidateOnStart`); off,
SEC-6 applies unchanged. Never set it in `appsettings.json`.

Blocked integration (OUT-7): `ReferralToolRules.BlockedReason(settings, latestStatus)` (pure,
unit-tested) says why an **enabled** integration can never deliver: a `ConnectionProblem`, or a 401/403
on the tenant's latest recorded `WebhookDelivery` (any kind). Disabled stays silent. It lights the
sidebar Integrations alert (`ShellSummary.IntegrationBlocked`, +1 in `AttentionCount`) and adds a danger
"Needs you" item on the dashboard with the reason. The latest-attempt lookup runs on every page and is
served by `IX_WebhookDeliveries_TenantId_Id` (`(TenantId, Id)`).

## Contract
The frozen ReferralTool contract is `docs/integration/referraltool-contract.md`. The status route is
`/v1.0/kafka/candidatestatusupdate` with dual `X-Api-Key` + `X-Auth-Token`; the vacancy calls are in
Appendix D.

## Integrations screen (redesign)
Rebuilt with a dark health banner (connection state, customer id, delivered/failed/pending counts, Test
connection), the connection form (masked write-only secrets: blank keeps the stored value), the
"Vacancies: Pushed to ReferralTool." card (published job count and the "Sync vacancies now" button,
`hx-confirm`), and an inline delivery-log preview. The full paged log lives on `Deliveries`; both render
rows through the shared `_DeliveryRows` partial, which labels vacancy messages "Vacancy sync" instead of
a candidate status.
