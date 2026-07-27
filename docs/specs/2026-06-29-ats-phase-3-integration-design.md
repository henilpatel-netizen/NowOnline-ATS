# Design Spec: ATS Phase 3 - ReferralTool Integration

- Date: 2026-06-29
- Status: Proposed (awaiting review)
- Author: Henil Patel (with Claude)
- Related: `2026-06-26-ats-product-design.md` (Sections 10, 12 Phase 3, 13), `referraltool-contract.md`
  (Appendices A/B/C), Phases 1-2 entities and services.

This spec was validated against the live ReferralTool source (`Logic/Import/CatsOne/*`,
`Api/Controllers/KafkaController.cs`, `Logic/CandidateCreator.cs`, `Logic/WebhookCandidateEventCreator.cs`).
Where the live code differs from the 2026-06-26 frozen contract, this spec follows the live code and
records the drift (Section 9).

---

## 1. Purpose and scope

Close the ReferralTool loop. Our jobs flow into ReferralTool through a CatsOne-compatible feed it pulls,
and each referred candidate's stage progression flows out to ReferralTool as a status update that
creates the candidate and credits the referrer. Built so ReferralTool needs **zero code changes**.

### In scope
- CatsOne-compatible vacancy feed in `Ats.Api` with a per-tenant hashed feed API key, pagination, and
  `Job.Status` to `Actief` mapping.
- `OutboxMessage` enqueue on referred `ApplicationEvent`s; `Ats.Worker` outbox drainer with
  per-application ordering, retries/backoff, and dead-lettering; a ReferralTool HTTP client.
- `WebhookDelivery` log and a back-office integration-settings UI.

### Out of scope (deferred to Phase 4)
- Email notifications, dashboards, and a "Test feed" button beyond the basics.
- The Recruitee HMAC webhook channel (we target only the generic `candidatestatusupdate` channel).

### Structure: one spec, three plans
- Plan A: vacancy feed + feed API key.
- Plan B: outbox + worker + ReferralTool client (the push loop).
- Plan C: integration-settings UI + delivery-log view.

---

## 2. Locked decisions

| Decision | Choice |
|---|---|
| Status-update channel | Generic `POST /v1.0/kafka/candidatestatusupdate`, flat JSON |
| Status-update auth | Both `X-Api-Key` (ReferralTool-issued) and `X-Auth-Token` (Kafka:AuthToken). Build to live code |
| Feed API key | Random 32-byte URL-safe key; store SHA-256 hash in `TenantSettings.FeedApiKeyHash`; constant-time compare; plaintext shown once |
| Vacancy-not-imported handling | Worker pre-flights `checkvacancyexists`; missing vacancy is transient (retry), not terminal |
| Feed custom_fields (hours) | Omitted in v1 (optional). Correct nested shape recorded for later |
| Worker polling | ~15s default, configurable |
| ExternalCandidateId | `Candidate.Key` (GUID, stable per candidate) |
| CandidateStatus | `PipelineStage.ReferralStatusOverride ?? PipelineStage.Name` |

---

## 3. ReferralTool contract as verified in source

### 3.1 Vacancy feed (ReferralTool pulls, `CatsOneImportClient` + `CatsOneMapper`)
- Request: `POST {ApiUrl}/jobs/search?per_page=100&page={n}`, header `Authorization: Token {feedKey}`,
  body `{ "and": [ { "field": "is_published", "filter": "exactly", "value": true } ] }`. The client
  paginates until `count` is 0 or `allVacancies.Count >= total`.
- Response per job (mapper reads): `id` (to ExternalId), `title` (to Name), `type` (row skipped unless
  `H`/`C2H`/`FL`), `location.city` (to Location), `_embedded.status.title` (Status; anything other than
  `"Actief"` marks the vacancy deleted/inactive). Hours come from
  `_embedded.custom_fields[]` where `_embedded.definition.name == "Aantal uren."`, value `"min-max"` or a
  single number. ReferralTool upserts vacancies by `(CustomerId, ExternalId)`.

### 3.2 Status update (we push, `KafkaController.CandidateStatusUpdate`)
- Route: `POST /v1.0/kafka/candidatestatusupdate`. Auth: `[Authorize(ApiKey)]` requires header
  `X-Api-Key` (a ReferralTool-issued key) AND a manual check that header `X-Auth-Token` equals
  ReferralTool's `Kafka:AuthToken`.
- Body `KafkaCreateCandidatePayload`: `Code` (required, max 36), `ExternalVacancyId` (required, max 36),
  `ExternalCandidateId` (required, max 36), `CandidateStatus` (optional), `CustomerId` (required).
- Behavior:
  - If no candidate exists for `(CustomerId, ExternalCandidateId)`, `CandidateCreator.Create` runs. It
    requires the **vacancy to already exist** for `(CustomerId, ExternalVacancyId)` (else a 400
    "Vacancy does not exist"), resolves the referrer/referral from `Code`
    (prefix `2` = referral, must belong to that vacancy; otherwise referrer), and links the candidate.
  - If `CandidateStatus` is present, `WebhookCandidateEventCreator.Create` requires it to match a seeded
    `CustomerEventType.Type` (case-insensitive, not hidden), rejects a **duplicate** event type for the
    candidate (400), creates the event, and credits the referrer points.
- There is also `POST /v1.0/kafka/checkvacancyexists` (same auth) taking `{ CustomerId, ExternalVacancyId }`
  and returning `{ exists: bool }`. We use it as a pre-flight.

### 3.3 Code prefixes (Appendix C)
Referrer prefix `1`, referral prefix `2`, both 9 chars. We forward `Application.SourceCode` verbatim;
ReferralTool does the prefix resolution. We never parse it.

---

## 4. Plan A: Vacancy feed (`Ats.Api`)

- Wire `AddAtsInfrastructure(config)` into `Ats.Api` (Phase 0 wired only Web). Remove the template
  WeatherForecast endpoint. Keep Serilog + `/health` (already present).
- `FeedController` with `POST /jobs/search` (query `per_page`, `page`). The body filter is accepted but
  the authoritative filter is server-side: return the tenant's non-Draft jobs.
- Response builder emits the CatsOne shape:
  `{ count, total, _embedded: { jobs: [ { id, type: "H", title, location: { city }, _embedded: { status: { title } } } ] } }`.
  `status.title` is `"Actief"` for Published and `"Gesloten"` (any non-Actief) for Closed, so ReferralTool
  deactivates closed vacancies. Draft jobs are never returned. `custom_fields` omitted in v1. Pagination
  uses `per_page`/`page` with `count` (this page) and `total` (all matching).
- Feed authentication: an `ApiKeyAuthFilter` (or minimal middleware) reads `Authorization: Token {key}`,
  computes SHA-256, finds the `TenantSettings` whose `FeedApiKeyHash` matches via an
  `IgnoreQueryFilters` query (no tenant resolved yet), constant-time-compares, sets
  `HttpContext.Items["TenantId"]`, and returns 401 on missing/invalid key. The global query filter then
  scopes the jobs query. This is a new documented filter-bypass spot.

Exit (Plan A): ReferralTool (or curl) can call `/jobs/search` with a tenant's feed key and receive that
tenant's published jobs in the CatsOne shape; closed jobs appear non-Actief; draft jobs never appear.

---

## 5. Plan B: Outbox, worker, and client (the push loop)

### 5.1 Entities
- `OutboxMessage : TenantEntity` - `ApplicationId`, `Code`, `ExternalVacancyId`, `ExternalCandidateId`,
  `CandidateStatus` (the payload snapshot, captured at enqueue), `Status` (Pending/Delivered/Failed),
  `Attempts`, `NextAttemptAt`, `LastError`, `RowVersion`. Snapshotting decouples delivery from later edits.
- `WebhookDelivery : TenantEntity` - `OutboxMessageId`, `AttemptedAt`, `Kind` (CheckVacancy/StatusUpdate),
  `HttpStatus`, `ResponseBody` (truncated), `Success`.

### 5.2 Enqueue
An `IOutboxEnqueuer` is called wherever an `ApplicationEvent` is created (the initial apply in
`CareerService.ApplyAsync` and back-office add, and forward moves in `ApplicationService.MoveStageAsync`).
It enqueues an `OutboxMessage` when all hold:
- the application has a non-empty `SourceCode`,
- the tenant's `TenantSettings.IntegrationEnabled` is true and `ReferralToolCustomerId` is set,
- this is the **first time** the application has reached the target stage (no prior `ApplicationEvent`
  with that `ToStageId`). Re-entering an already-reached stage (a backward move) does not enqueue.

The initial "Applied" event enqueues too, because that first status is what creates the candidate in
ReferralTool. The payload is snapshotted: `Code = SourceCode`, `ExternalVacancyId = Job.ExternalRef`,
`ExternalCandidateId = Candidate.Key.ToString("D")`, `CandidateStatus = stage.ReferralStatusOverride ?? stage.Name`.
Enqueue happens in the same unit of work as the move, so a move and its outbox row commit atomically.

### 5.3 Worker (`Ats.Worker`)
- Wire `AddAtsInfrastructure`. A `BackgroundService` (`OutboxDrainer`) polls every
  `Integration:PollSeconds` (default 15).
- Each cycle it claims due Pending messages across all tenants via an `IgnoreQueryFilters` query
  (`Status == Pending && NextAttemptAt <= now`), ordered by `(ApplicationId, Id)`. The worker is a
  trusted cross-tenant processor; this is a documented bypass.
- A settable `WorkerTenantContext : ITenantContext` is set to each message's `TenantId` before
  processing, so per-tenant queries (settings), `TenantId` stamping (the `WebhookDelivery` insert), and
  the filter all behave correctly for that tenant.
- **Per-application ordering:** messages for one application are processed oldest-first; on a transient
  failure or a deferral for that application, the worker stops processing that application's remaining
  messages this cycle (so message N+1 never goes before N succeeds).
- **Delivery for one message:**
  1. Load the tenant's integration settings; if disabled or incomplete, defer (transient).
  2. Pre-flight `checkvacancyexists`. If `exists == false`, the vacancy is not imported yet: defer as
     transient (backoff, no status sent). Log a `WebhookDelivery` (Kind=CheckVacancy).
  3. If it exists, `POST candidatestatusupdate`. `2xx` -> `Delivered`. `5xx`/timeout/network ->
     transient. `4xx` -> terminal (duplicate event, unmapped status, bad code). Log a `WebhookDelivery`
     (Kind=StatusUpdate) with status + truncated body.
- **Backoff and dead-letter:** transient failures increment `Attempts` and set
  `NextAttemptAt = now + min(cap, base * 2^Attempts)` (cap and base configurable; cap ~30 min). After
  `Integration:MaxAttempts` (configurable, default high enough to span the feed-import window), park as
  `Failed`. Terminal failures set `Failed` immediately.
- Concurrency uses `RowVersion`; a single worker instance is expected, but the rowversion guards against
  double-processing.

### 5.4 ReferralTool client (Infrastructure)
A typed `HttpClient` (`IReferralToolClient`) with `CheckVacancyExistsAsync` and
`SendStatusUpdateAsync`, posting to `{ReferralToolBaseUrl}/v1.0/kafka/{action}` with headers
`X-Api-Key: {TenantSettings.ReferralToolApiKey}` and `X-Auth-Token: {TenantSettings.ReferralToolAuthToken}`,
returning the HTTP status and body for logging. Per-tenant base URL and credentials are passed in by the
worker (read from settings), not from a single app config.

---

## 6. Plan C: Integration settings UI + delivery log (back-office)

- Integration-settings page (Owner-only) editing `TenantSettings`: `IntegrationEnabled`,
  `ReferralToolBaseUrl`, `ReferralToolAuthToken`, the new `ReferralToolApiKey`, `ReferralToolCustomerId`,
  `CodeParameterName`, plus Generate/Regenerate the **feed API key** (plaintext shown once; only the
  SHA-256 hash persisted). Credential fields are masked on display.
- Delivery-log view (read-only): the tenant's `OutboxMessage`s with status/attempts and their
  `WebhookDelivery` rows (what we sent, what ReferralTool answered), newest first, filterable by status.
- Sidebar gains an "Integration" entry.

---

## 7. Data and schema changes

- New entities `OutboxMessage`, `WebhookDelivery`. New column `TenantSettings.ReferralToolApiKey`
  (outbound credential, stored for use alongside `ReferralToolAuthToken`).
- One migration (in Plan B) creates the two tables and adds the column. Created by the AI, applied by the
  developer.
- Indexes: `OutboxMessage` index `(TenantId, Status, NextAttemptAt)` for the drain query and
  `(TenantId, ApplicationId, Id)` for ordering; `WebhookDelivery` index `(TenantId, OutboxMessageId)`.

---

## 8. Security (OWASP-aligned)

- Feed key stored only as SHA-256 hash; constant-time comparison; 401 on bad key; the key resolves the
  tenant and the global filter does the rest (a tenant's key can only ever read that tenant's jobs).
- Outbound credentials (`ReferralToolApiKey`, `ReferralToolAuthToken`) are per-tenant secrets stored in
  `TenantSettings`; masked in the UI; never logged. (Encryption-at-rest is a later hardening note.)
- Only referred applications (`SourceCode` present) emit; organic applies never leave the system.
- Idempotency: each `OutboxMessage` is one (application, stage) transition; combined with ReferralTool's
  duplicate-event guard and our first-arrival enqueue rule, retries are safe.
- `WebhookDelivery` stores response bodies truncated and is tenant-scoped and back-office-only.

---

## 9. Frozen-contract drift recorded (update `referraltool-contract.md`)

1. Status route confirmed as `/v1.0/kafka/candidatestatusupdate` (controller `Kafka`, version 1.0).
2. Status endpoint now also requires `X-Api-Key` (ReferralTool-issued) in addition to `X-Auth-Token`.
3. "Vacancy does not exist" is returned as HTTP 400 (transient until import); `checkvacancyexists` exists
   as a pre-flight.
4. Feed `custom_fields` hours use a nested `_embedded.definition.name` shape, not the flat `{name,value}`
   shown in the 2026-06-26 contract. v1 omits the field.

The frozen contract file will be updated to reflect 1-4 explicitly, since the contract is the source of
truth and changes must be recorded.

---

## 10. End-to-end verification (spec Section 13 loop)

One-time ReferralTool setup (developer, on the ReferralTool side): a test Customer with seeded event
types matching our stage statuses, an `ImportSetting` (`ImportType = CatsOne`) pointing at our feed URL
with the feed key and a `VacancySiteUrlTemplate` pointing at our career site, an issued `X-Api-Key`, and
`Kafka:AuthToken`. In our back-office: fill integration settings (base URL, `X-Auth-Token`, `X-Api-Key`,
`CustomerId`), generate the feed key, enable integration.

Loop:
1. Publish a job; ReferralTool pulls the feed; the vacancy appears.
2. Share it in ReferralTool (referrer code `1...`); open the share link -> our career site job page with
   `?ref=`.
3. Apply on the career site; `SourceCode` is captured.
4. Move the candidate forward; the worker pre-checks the vacancy, posts the status update; ReferralTool
   creates the candidate and credits the referrer.
5. Repeat moves (1st -> 2nd -> Hired); each fires another update and credits more.
6. The delivery log shows each request and ReferralTool's response.

---

## 11. Documentation maintenance (spec Section 16)

After Phase 3 lands: add `.claude/skills/integration/SKILL.md` (feed, feed-key auth, outbox/worker,
delivery semantics, ReferralTool client); update `.claude/rules/multi-tenancy.md` with the two new
documented bypasses (feed-key resolver, worker cross-tenant drain); update
`docs/integration/referraltool-contract.md` per Section 9; refresh the `CLAUDE.md` skill-index; keep
`docs/specs` and `docs/plans` current.

---

## 12. Notes

- Restrictions unchanged: the AI does not commit, apply migrations, or deploy. Phase 3 adds one migration
  (Plan B), applied by the developer.
- Em dashes and emoji are avoided in all generated content per the working conventions.
