---
name: integration
description: The Ats - ReferralTool integration - vacancy feed, feed-key auth, outbox enqueue, the worker delivery loop, the ReferralTool client, and integration settings. Read before changing any integration behavior.
---

# Ats - ReferralTool Integration (Phase 3)

## Vacancy feed (Ats.Api)
`POST /jobs/search` returns the tenant's non-draft jobs in the CatsOne shape (`type":"H"`,
`location.city`, `status.title` Actief for Published, non-Actief for Closed; Draft excluded). Auth is a
per-tenant feed key sent as `Authorization: Token {key}`; `FeedApiKeyFilter` SHA-256-matches
`TenantSettings.FeedApiKeyHash` (IgnoreQueryFilters) and sets `HttpContext.Items["TenantId"]`. Dev-only
Scalar UI at `/scalar`.

## Outbox enqueue
`IOutboxEnqueuer.StageAsync` adds an `OutboxMessage` (payload snapshot) in the same unit of work as the
stage `ApplicationEvent`, only on first arrival at a stage, when the application has a `SourceCode` and
`TenantSettings.IntegrationEnabled` with a `ReferralToolCustomerId`. Wired into
`ApplicationService.MoveStageAsync`/`CreateApplicationAsync` and `CareerService.ApplyAsync`. Mapping:
`Code`=SourceCode, `ExternalVacancyId`=Job.ExternalRef, `ExternalCandidateId`=Candidate.Key,
`CandidateStatus`=stage.ReferralStatusOverride ?? stage.Name.

## Worker delivery (Ats.Worker)
`OutboxDrainer` polls every `Integration:PollSeconds`. `OutboxClaimStore` claims due Pending messages
across all tenants (IgnoreQueryFilters); per message the `OutboxProcessor` sets
`WorkerTenantContext.TenantId`, pre-checks the vacancy (`checkvacancyexists`), and posts
`candidatestatusupdate` via `IReferralToolClient` with `X-Api-Key` + `X-Auth-Token`. Every attempt logs
a `WebhookDelivery`. The outcome rules live in `ReferralToolRules` (Application, pure, unit-tested;
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
- A 2xx vacancy check with an unreadable body defers with its own error, not "not imported yet" (OUT-5).

Per message, `OutboxBatch.RunAsync` (Application, unit-tested) isolates failures: an exception is logged
with `MessageId`/`TenantId`, recorded through `IOutboxProcessor.RecordFailureAsync` in a fresh scope (same
defer rules; `LastError` holds only the exception type, the detail stays in the worker log), and the
other claims carry on. There are no per-application chains: the claim already returns at most one message
per application, so ordering is enforced there. Shutdown cancellation propagates and is never counted (OUT-2).

**Multi-instance safe (Phase 3, DATA-3).** `OutboxClaimStore` claims due messages atomically in one
statement (`UPDATE ... WITH (READPAST, UPDLOCK, ROWLOCK) ... SET Status = Processing OUTPUT inserted.*`),
so two worker instances can never claim the same row. A claim sets `NextAttemptAt` to a visibility-timeout
lease (`Integration:ClaimLeaseSeconds`, default 300s); a crashed worker's `Processing` messages are
reclaimed once the lease elapses. `OutboxMessage.RowVersion` remains a concurrency token.

**Ordering + fencing (phase 10, OUT-4).** The claim skips any message with an older `Pending`/`Processing`
message for the same `(TenantId, ApplicationId)` (`NOT EXISTS ... WITH (READCOMMITTEDLOCK)`, which must
block on locked rows and must not read RCSI versions; Delivered/Failed predecessors do not block). Cost: one
message per application per poll cycle. Each `OutboxClaim` carries its `Lease` (`OUTPUT inserted.NextAttemptAt
AS Lease`) as a fencing token: `OutboxBatch` skips claims whose lease has expired, and the processor only
touches a row that is `Processing` with that exact lease (`OutboxClaim.IsOwnedBy`). Remaining window: a
lease expiring during one in-flight call can cause a duplicate send, which ReferralTool's duplicate guard
absorbs. Keep `ClaimLeaseSeconds` above a typical batch's run time. Manual SQL Server verification steps:
`docs/review/phase-10-outbox-delivery-correctness.md`.

## ReferralTool-side setup (required before the loop runs; data/config only, no RT code change)
The integration is wire-compatible with ReferralTool, but the developer must configure ReferralTool and
fill the ATS integration settings to match:
1. In ReferralTool, create an `ImportSetting` for the customer: `ImportType = CatsOne`,
   `ApiUrl = https://<ats-api-host>` (so it POSTs `{ApiUrl}/jobs/search`), `ApiKey = <the ATS feed key
   plaintext>` (generated on the ATS Integration page; only its hash is stored in the ATS), and a
   `VacancySiteUrlTemplate` pointing at the ATS career site.
2. Set ReferralTool config `Kafka:AuthToken`; put the same value in the ATS `ReferralToolAuthToken`.
3. The ATS `ReferralToolApiKey` must be a valid ReferralTool **Customer ApiKey GUID** (ReferralTool
   parses `X-Api-Key` as a GUID; a non-GUID 401s and the queue is postponed until the key is fixed).
4. Seed a ReferralTool `CustomerEventType` for every pipeline stage that should award points, matching
   the stage's `ReferralStatusOverride ?? Name` (case-insensitive). Unmatched statuses 400 and never
   accrue points; prefer pinning `ReferralStatusOverride` to the exact ReferralTool event-type strings.
Operational note: a stale/invalid `?ref=` code makes the first status update 400 and the message goes
`Failed` permanently (the candidate is never created in ReferralTool). Monitor `Failed` in the
Integration delivery log (filter by state).

## Settings and log (back-office, Owner-only)
`IntegrationController` edits `TenantSettings` integration fields and generates the feed key (hash
stored, plaintext shown once). `Deliveries` shows recent `OutboxMessage`s with their `WebhookDelivery`
attempts. After a successful save with the integration enabled, `UpdateAsync` pulls `NextAttemptAt`
forward to now on this tenant's postponed `Pending` messages (tracked update, never `Processing` rows, a
concurrency conflict is ignored), so a fix is retried on the next poll instead of after `MaxBackoffSeconds`.

Blocked integration (OUT-7): `ReferralToolRules.BlockedReason(settings, latestStatus)` (pure,
unit-tested) says why an **enabled** integration can never deliver: a `ConnectionProblem`, or a 401/403
on the tenant's latest recorded `WebhookDelivery` (any kind). Disabled stays silent. It lights the
sidebar Integrations alert (`ShellSummary.IntegrationBlocked`, +1 in `AttentionCount`) and adds a danger
"Needs you" item on the dashboard with the reason. The latest-attempt lookup runs on every page and is
served by `IX_WebhookDeliveries_TenantId_Id` (`(TenantId, Id)`).

## Contract
The frozen ReferralTool contract is `docs/integration/referraltool-contract.md`. The status route is
`/v1.0/kafka/candidatestatusupdate` with dual `X-Api-Key` + `X-Auth-Token`.

## Feed pull telemetry (redesign)
`TenantSettings.FeedLastPulledAt` records when the vacancy feed was last pulled, for the "feed pulled
N min ago" line on the dashboard and integrations screens. It is written by the feed endpoint
(`Ats.Api` `FeedController`) after `FeedApiKeyFilter` resolves the tenant, debounced to at most once a
minute, and wrapped so a telemetry-write failure never fails the feed response. It is display-only
and not part of the frozen contract. It is wired: `FeedController.Search` (`Ats.Api`) calls
`IVacancyFeedRepository.TouchFeedPulledAsync` after building the response, debounced by
`FeedPullThrottle` (at most one write per minute) and wrapped in try/catch so a telemetry-write
failure never fails the feed. It surfaces on the dashboard card and the Integrations health banner.

## Integrations screen (redesign)
Rebuilt with a dark health banner (connection state, customer id, feed-pull age, 24h delivered/
failed/pending, Test connection), the connection form (masked write-only secrets: blank keeps the
stored value), the feed-key card, and an inline delivery-log preview. The full paged log lives on
`Deliveries`; both render rows through the shared `_DeliveryRows` partial.
