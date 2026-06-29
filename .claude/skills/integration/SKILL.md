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
`candidatestatusupdate` via `IReferralToolClient` with `X-Api-Key` + `X-Auth-Token`. Outcomes: 2xx
Delivered; 5xx/timeout transient (exponential backoff, dead-letter after `MaxAttempts`); 4xx terminal;
vacancy-not-imported is transient. Per-application ordering: stop a chain on the first non-delivered.
Every attempt logs a `WebhookDelivery`.

## Settings and log (back-office, Owner-only)
`IntegrationController` edits `TenantSettings` integration fields and generates the feed key (hash
stored, plaintext shown once). `Deliveries` shows recent `OutboxMessage`s with their `WebhookDelivery`
attempts.

## Contract
The frozen ReferralTool contract is `docs/integration/referraltool-contract.md`. The status route is
`/v1.0/kafka/candidatestatusupdate` with dual `X-Api-Key` + `X-Auth-Token`.
