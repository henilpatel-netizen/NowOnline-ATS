# Design Spec: Multi-Tenant ATS Product ("Ats")

- Date: 2026-06-26
- Status: Approved (product shape + Phase 0 scope)
- Author: Henil Patel (with Claude)
- Related system: ReferralTool (`D:\LiveProject\ReferralTool`)

---

## 1. Purpose & scope

Build a standalone, multi-tenant **Applicant Tracking System (ATS)** as a product. Companies
register as tenants, define hiring pipelines, post jobs, host a public career site, and move
candidates through stages. Referral sourcing is a first-class capability: jobs flow to ReferralTool
via a CatsOne-compatible feed, the career site captures a referral code on apply, and every stage
transition pushes a status update to ReferralTool.

A direct consequence of building this correctly is that it becomes the **end-to-end test rig** for
ReferralTool's referral loop, which is currently untestable because the real job sites and ATSs are
third-party. But the design goal is a sellable product, not a fixture.

### In scope (v1)
Tenancy + onboarding, identity/RBAC, pipeline templates, jobs + publish lifecycle, candidates,
applications, stage transitions, public career site with apply + resume upload + referral-code
capture, CatsOne-compatible vacancy feed, outbound status-update delivery (outbox + worker),
per-tenant integration settings, basic audit + notifications.

### Out of scope (v1)
Subdomains / custom domains, billing/subscriptions, CV parsing, email campaigns, advanced analytics,
the Recruitee HMAC webhook channel (we target only the generic `candidatestatusupdate` channel).

---

## 2. Technology

- .NET 10, ASP.NET Core MVC
- SQL Server, EF Core (latest)
- ASP.NET Core Identity (behind an abstraction), cookie auth (back-office) + JWT (API)
- FluentValidation, Asp.Versioning
- Background worker (hosted service) for outbox delivery + notifications
- Structured logging (Serilog); health checks per host

---

## 3. Locked decisions

| Decision | Choice |
|---|---|
| Multi-tenancy isolation | Shared DB + `TenantId` discriminator + EF Core global query filters + SaveChanges interceptor that stamps `TenantId` |
| Authentication | Abstracted behind `IIdentityService`; first impl ASP.NET Core Identity; swappable to Auth0/Entra later |
| Pipeline configurability | Per-tenant reusable templates, selected per job; stage names map to ReferralTool `CandidateStatus` |
| Vacancy ingestion to ReferralTool | ATS exposes a **CatsOne-compatible feed**; zero ReferralTool code changes |
| Status-update channel | Flat JSON `POST /v1.0/candidatestatusupdate` + `X-Auth-Token` header (generic/Kafka channel) |
| Career site addressing | Path-based: `/careers/{tenantSlug}` |

---

## 4. Solution structure

```
Ats.sln
 ├─ Ats.Web            MVC back-office (/manage) + public career site (Areas: Manage, Careers)
 ├─ Ats.Api            REST API: CatsOne-compatible vacancy feed + integration endpoints
 ├─ Ats.Domain         Entities, aggregates, enums, domain rules (no EF dependency)
 ├─ Ats.Application    Use-case services, DTOs, validators, IIdentityService, IReferralIntegration
 ├─ Ats.Infrastructure EF Core DbContext, repositories, tenancy interceptor, Identity impl, outbox, email, ReferralTool client, IFileStore
 └─ Ats.Worker         Background host: outbox drainer, notifications, retries
```

Layering mirrors ReferralTool: Controllers -> Application services -> Repositories -> EF.

---

## 5. Multi-tenancy (the spine)

- `ITenantEntity { int TenantId }`; `TenantEntity` base class carries it.
- `ITenantContext` resolves the current tenant: from the user claim (back-office/API) or from the
  `{tenantSlug}` route value (career site).
- EF Core **global query filters** auto-apply `e.TenantId == _tenant.Current` to every tenant entity.
- A `SaveChangesInterceptor` **stamps `TenantId` on insert**, so application code never sets it by
  hand. This structurally avoids ReferralTool's "Admin writes NULL DataKey" foot-gun.
- Tenant resolution is mandatory on tenant-scoped routes; unresolved tenant = rejected request.
  No global fallback that could leak across tenants.

---

## 6. Identity (abstracted)

- `IIdentityService` (sign-in, create user, reset, role checks) in `Ats.Application`.
- First impl: ASP.NET Core Identity on EF Core. Cookie auth for `/manage`, JWT bearer for `Ats.Api`.
- Per-tenant roles: **Owner / Recruiter / HiringManager / Viewer**, enforced via policy handlers.
- Swappable later without touching domain or use-cases.

---

## 7. Domain model

**Tenant context**
- `Tenant` (Company): `Name`, `Slug` (unique, used in career URLs), `Status`, timestamps.
- `TenantSettings`: branding basics + integration config (see Section 10).
- `AppUser`, `Role`, `UserRole` (per-tenant RBAC).
- `Department`, `Location`.

**Recruiting**
- `PipelineTemplate` -> ordered `PipelineStage[]` (`Name`, `Order`, `IsTerminal`, `TerminalOutcome` Hired/Rejected/null).
- `Job`: `Title`, `Description`, `DepartmentId`, `LocationId`, `EmploymentType`, `PipelineTemplateId`,
  `Status` (Draft/Published/Closed), `PublishedAt`, `ExternalRef` (stable id exposed in the feed).
- `Candidate` (person, tenant-scoped): `FirstName`, `LastName`, `Email`, `Phone`, `ResumeFileKey`.
- `Application` (Candidate x Job): `CurrentStageId`, `SourceCode` (captured referral code),
  `AppliedAt`, `Status`.
- `ApplicationEvent`: `ApplicationId`, `FromStageId?`, `ToStageId`, `OccurredAt`, `MovedByUserId?`.
  This is the stage-history audit trail AND the trigger for outbound status updates.

**Integration**
- `OutboxMessage`: durable record of each status-update to deliver (payload, target, attempts, status, next-retry).
- `WebhookDelivery`: per-attempt log (HTTP status, response body, timestamp) for observability.

The three identifiers ReferralTool needs map to: `Application.SourceCode` (the code),
`Job.ExternalRef` (external vacancy id), `Candidate` external id (external candidate id).

### 7.1 Identifier generation & constraints
- `Job.ExternalRef`: generated at create time, stable, unique per tenant, URL-safe, **max 36 chars**
  (ReferralTool `ExternalVacancyId` limit). Format e.g. `JOB-{tenantSequence}`. Never reused after delete.
- Candidate external id (sent as `ExternalCandidateId`): **max 36 chars**. Use the candidate's
  `Guid Key` (`ToString("D")` = 36 chars). Never send raw email/PII as the external id.
- `Application.SourceCode`: stored verbatim from the career-site query param, trimmed, **max 36 chars**
  (ReferralTool `Code` limit). Empty/whitespace = "no code" (organic apply, no status emission).

### 7.2 Application rules
- One active `Application` per (Candidate email, Job) per tenant. Re-submitting updates the existing
  application instead of creating a duplicate (mirrors ReferralTool's candidate-by-externalId upsert
  and avoids duplicate candidate creation downstream).
- Backward stage moves are allowed in the UI for correction but **do not emit** a status update
  (ReferralTool has a once-per-event-type duplicate guard and no demote concept).
- A status update is emitted only the **first time** an application reaches a given stage. Re-entering
  a previously-sent stage does not re-emit.
- `Application` carries a `RowVersion`; stage moves use optimistic concurrency so two recruiters
  cannot double-fire the same transition.

---

## 8. Pipeline -> ReferralTool event vocabulary

Stage `Name` strings become the `CandidateStatus` sent to ReferralTool. ReferralTool matches
`CandidateStatus` case-insensitively against its `CustomerEventType.Type`. To keep the systems
decoupled, each tenant has an optional **stage -> status mapping** (defaults to the stage name) so a
tenant whose ReferralTool customer uses `"Hired"` can map a `"Aangenomen"` stage to `"Hired"`.

---

## 9. Career site + application capture

- Routes: `/careers/{tenantSlug}` (board), `/careers/{tenantSlug}/jobs/{externalRef}` (detail),
  `POST .../apply`.
- Job detail accepts `?{codeParam}={code}` (e.g. `?ref=1RR123456`). The apply form carries that
  value through a hidden field and stores it on `Application.SourceCode`.
- Apply: create/match `Candidate` by email within tenant, create `Application` at the pipeline's
  first stage, write the initial `ApplicationEvent`, upload resume via `IFileStore`
  (local disk dev / Azure Blob prod).
- Only `Published` jobs are visible; tenant resolved from slug.

---

## 10. Integration layer (the ReferralTool bridge)

### 10a. Vacancy feed (CatsOne-compatible) -- ATS serves, ReferralTool pulls
`Ats.Api` exposes `POST /jobs/search?per_page=100&page={n}`. See frozen contract in Appendix A.
ReferralTool builds the vacancy URL itself from its `ImportSetting.VacancySiteUrlTemplate`; we give
the client a template pointing at our career site
(`https://ourats.com/careers/{slug}/jobs/{vacancyId}`). No ReferralTool code change.

Feed contents / `Job.Status` mapping:
- `Published` jobs appear with `_embedded.status.title = "Actief"` (active in ReferralTool).
- `Closed`/unpublished jobs appear with a non-`Actief` status so ReferralTool deactivates the matching
  vacancy. `Draft` jobs are never exposed.
- `type` is emitted as `H` for all jobs in v1 (must be `H`/`C2H`/`FL` or ReferralTool skips the row).
- Pagination: honour `page` + `per_page`; return `count`/`total` so the client loops correctly.

### 10b. Status updates -- ATS pushes
On each `ApplicationEvent`, enqueue an `OutboxMessage`. `Ats.Worker` drains the outbox and POSTs to
ReferralTool per Appendix B. Applications with no `SourceCode` (organic, non-referred) skip emission.

Delivery semantics (critical):
- **Per-application ordering.** Updates for one application MUST be delivered in stage order.
  ReferralTool creates the Candidate on the FIRST status it receives for an unknown
  `ExternalCandidateId`; an out-of-order delivery would create the candidate at the wrong stage and the
  duplicate guard could drop the intended first event. The worker delivers an application's messages
  sequentially and does not send message N+1 until N is confirmed.
- **Vacancy-exists dependency.** A status update fails if ReferralTool has not yet imported the job
  (`ExternalVacancyId` unknown). This is expected when a candidate is moved before the next scheduled
  import. Outbox retry (exponential backoff) covers it; an optional on-demand feed pull shortens the window.
- **Retries & dead-letter.** Bounded retries with exponential backoff. 5xx/timeouts are retried;
  4xx (validation/duplicate) is terminal and not retried. After max attempts a message is parked as
  `Failed` (dead-letter) and surfaced in the delivery log for manual inspection.
- **Idempotency.** Each outbox message is exactly one (application, stage) transition, sent at most
  once on success; combined with ReferralTool's duplicate guard, retries are safe.

### 10c. Per-tenant integration config (`TenantSettings`)
`ReferralToolBaseUrl`, `ReferralToolAuthToken`, `ReferralToolCustomerId` (maps tenant -> ReferralTool
Customer), `CodeParameterName` (default `ref`, must match ReferralTool's `Customer.CodeParameterName`),
`FeedApiKey`, `IntegrationEnabled`.

---

## 11. Cross-cutting

- Validation: FluentValidation in the Application layer.
- API versioning: Asp.Versioning, v1.0 baseline.
- Audit: back-office action log + `ApplicationEvent` history + `WebhookDelivery` log.
- Notifications: email on new application / stage change behind `IEmailSender` (dev sink locally).
- Security (OWASP-aligned): constant-time token/API-key comparison; resume upload type/size
  validation; career-site inputs treated as untrusted; outbound URLs are tenant-config, not user input.

### 11.1 Non-functional requirements
- **Time:** store all timestamps as UTC `DateTimeOffset`; render in the viewer's locale.
- **Soft delete:** tenant-scoped business entities (Job, Candidate, Application) use soft delete;
  hard-delete only where explicitly required.
- **Concurrency:** `RowVersion` on `Application` (and `OutboxMessage`) for optimistic concurrency.
- **Observability:** structured logging with a per-request correlation id; the `WebhookDelivery`
  log records every outbound call's request + response.
- **Health checks:** `/health` (DB + worker reachability) on each host.
- **Config & secrets:** per-environment `appsettings`; secrets (DB connection, feed API keys,
  ReferralTool auth token) outside source control (user-secrets in dev, env vars / Key Vault in prod).
  Feed API keys stored **hashed**, compared constant-time.
- **Migrations:** EF Core migrations live in `Ats.Infrastructure` and are applied **manually** by a
  developer (never auto-applied), matching the ReferralTool working convention.

---

## 12. Phased delivery (each gets its own spec + plan)

### Phase 0 - Foundation
- Deliverables: solution + 6 projects; `TenantEntity` base + `ITenantContext` + global query filters +
  SaveChanges `TenantId` interceptor; `IIdentityService` + ASP.NET Core Identity impl (cookie + JWT);
  roles Owner/Recruiter/HiringManager/Viewer + policy handlers; `DbContext` + initial migration;
  tenant onboarding (register company + first Owner) with slug uniqueness + reserved-slug guard;
  per-tenant seeding of a default pipeline template on signup; Serilog + `/health` skeleton.
- Exit: a new company can register, sign in, and see an empty dashboard; every tenant query is
  auto-filtered and every insert auto-stamps `TenantId` (verified by a manual cross-tenant check).

### Phase 1 - Core ATS
- Deliverables: pipeline template CRUD (stages, order, terminal outcomes, stage->status mapping);
  department/location CRUD; job CRUD + Draft/Published/Closed lifecycle + `ExternalRef` generation;
  candidate + application aggregates; candidate board (kanban) + list; stage move with `RowVersion`
  concurrency + `ApplicationEvent` history; one-active-application-per-(candidate,job) rule.
- Exit: a recruiter can define a pipeline, publish a job, manually add a candidate, and move them
  through stages with full, ordered history.

### Phase 2 - Career site
- Deliverables: path-based `/careers/{slug}`; public board of published jobs; job detail reading
  `?{codeParam}=`; apply form (name/email/phone/resume) with hidden `ref` field; `IFileStore`
  (local in dev) resume upload with type/size validation; candidate create/match by email; initial
  `Application` + first `ApplicationEvent`.
- Exit: a candidate can browse, open a job via a `?ref=` link, apply, and the application is created
  with `SourceCode` captured.

### Phase 3 - Integration (closes the ReferralTool loop)
- Deliverables: CatsOne-compatible feed endpoint (Appendix A) with per-tenant hashed API key,
  pagination, and `Job.Status` mapping; outbox enqueue on `ApplicationEvent`; `Ats.Worker` outbox
  drainer with per-application ordering, retries/backoff, dead-letter; ReferralTool HTTP client;
  `WebhookDelivery` log; integration settings UI (Appendix B/C config).
- Exit: the full end-to-end loop (Section 13) works against a configured ReferralTool test customer;
  status updates land, points credit, and the delivery log shows request + response.

### Phase 4 - Polish
- Deliverables: email notifications (new application, stage change); audit views; basic dashboards
  (counts per stage/job); API-key admin (regenerate + "Test feed" button).
- Exit: product-grade back-office usability.

---

## 13. End-to-end test walkthrough (the priority)

One-time setup (manual, on ReferralTool side):
1. Create/pick a test Customer in ReferralTool; note its `CustomerId`.
2. Seed its event types (Shared, Applied, 1st Interview, 2nd Interview, Hired) with point values.
3. Add an `ImportSetting` for that Customer pointing at the ATS feed URL + API key.
4. In the ATS, fill integration settings: ReferralTool base URL, `X-Auth-Token`, matching `CustomerId`.

Loop:
1. Create + publish a job in the ATS.
2. ReferralTool pulls it; it appears as a vacancy.
3. Share the vacancy in ReferralTool (referrer code `1...`); ReferralTool awards "Shared" points (ATS not involved).
4. Open the share link -> redirects to the ATS career site job page with `?ref=1RR...`.
5. Apply on the career site; ATS stores the code on `Application.SourceCode`.
6. Move the candidate forward in the ATS board; ATS POSTs `candidatestatusupdate` to ReferralTool.
7. ReferralTool creates the candidate, links to the referrer, credits the stage points.
8. Repeat moves (1st -> 2nd -> Hired); each fires another update and credits more points.

The ATS `WebhookDelivery` log shows exactly what was sent and what ReferralTool answered, removing
the "is it us or them" guesswork.

---

## 14. Assumptions to confirm during implementation

1. Exact ReferralTool inbound route/version (controller declares `[HttpPost("candidatestatusupdate")]`;
   confirm the full path + version prefix before Phase 3 wiring).
2. ReferralTool side is configured by the developer (ImportSetting + Customer + seeded event types);
   the ATS produces a compatible feed/payload but never modifies ReferralTool.
3. CatsOne `custom_fields` (hours) are optional; the feed emits a minimal valid shape.

---

## 16. Documentation & knowledge structure (CLAUDE.md + skills)

The Ats repo carries its own AI-facing knowledge base, following the same pattern proven in
ReferralTool: a lean root `CLAUDE.md` plus a `.claude/` tree. The structure is grown phase by phase,
not written up front, so it always describes code that actually exists.

```
D:\LiveProject\Ats\
 ├─ CLAUDE.md                        # high-signal: overview, build/run, layering, conventions, skill index
 ├─ .claude\
 │   ├─ rules\                       # hard constraints, one concern per file
 │   │   ├─ restrictions.md          # AI never commits / applies migrations / deploys
 │   │   ├─ multi-tenancy.md         # TenantEntity + global filter + interceptor + onboarding stamping
 │   │   └─ migrations.md            # EF migrations are manual; location + naming
 │   └─ skills\                      # read-before-exploring domain references (frontmatter: name, description)
 │       ├─ architecture\SKILL.md    # solution layout, layering, DI, where code goes   (Phase 0)
 │       └─ multitenancy\SKILL.md    # tenancy spine reference                           (Phase 0)
 └─ docs\
     ├─ specs\        # this design spec (copied in)
     ├─ plans\        # per-phase implementation plans
     └─ integration\referraltool-contract.md   # frozen Appendices A-C
```

**Best-practice rules for these files**
- `CLAUDE.md` stays lean and high-signal: overview, build/run, layering, the multi-tenancy invariant,
  restrictions, and a skill-index table that points to `.claude/skills/*`. It is not a changelog and
  not an exhaustive manual.
- Each `.claude/rules/*.md` covers exactly one hard constraint.
- Each `.claude/skills/<domain>/SKILL.md` is the authoritative, read-first reference for one domain,
  with `name` + `description` frontmatter. New skills are added when a phase first creates that domain.
- Comments-in-code stay minimal (only what the code cannot say). Knowledge that helps future work
  lives in skills/rules, not in code comments.

**Documentation maintenance (mandatory, per phase)**
The final task of every phase plan updates the knowledge base:
1. Refresh the `CLAUDE.md` skill-index table and any changed conventions.
2. Add or update the `SKILL.md` for the domain that phase built
   (Phase 1 -> `entities` + `pipeline`; Phase 2 -> `career-site`; Phase 3 -> `integration`;
   Phase 4 -> `notifications`/`audit` as needed).
3. Keep `docs/specs` and `docs/plans` current.

This is also why each phase is planned in the Ats session after the previous phase lands: the planner
reads the real Phase-0+ code and the current skills, and produces concrete steps rather than guesses.

---

# Appendix A -- ReferralTool integration contract: VACANCY FEED (frozen)

Source of truth at design time:
- `Logic/Import/CatsOne/CatsOneImportClient.cs`
- `Logic/Import/CatsOne/CatsOneMapper.cs`
- `Logic/Import/ImportVacancyDto.cs`

**Request ReferralTool makes (the ATS must answer this):**
- Method: `POST`
- URL: `{ImportSetting.ApiUrl}/jobs/search?per_page=100&page={page}` (page increments until a page
  returns fewer than 100 rows)
- Auth header: `Authorization: Token {ImportSetting.ApiKey}` (static API key the ATS issues per tenant)
- Body (filter for published jobs):
  ```json
  { "and": [ { "field": "is_published", "filter": "exactly", "value": true } ] }
  ```

**Response the ATS must return:**
```json
{
  "count": 1,
  "total": 1,
  "_embedded": {
    "jobs": [
      {
        "id": "JOB-1042",
        "type": "H",
        "title": "Senior .NET Developer",
        "location": { "city": "Amsterdam" },
        "_embedded": {
          "status": { "title": "Actief" },
          "custom_fields": [
            { "name": "Aantal uren.", "value": "32-40" }
          ]
        }
      }
    ]
  }
}
```

Mapping applied by ReferralTool's `CatsOneMapper`:

| Feed field | ReferralTool field | Notes |
|---|---|---|
| `id` | `ExternalId` | Must equal `Job.ExternalRef`. Stable, unique per tenant. |
| `title` | Vacancy title | |
| `type` | (filter) | Row is SKIPPED unless `type` is `H`, `C2H`, or `FL`. Emit `H` by default. |
| `location.city` | `Location` | |
| `_embedded.status.title` | status | `"Actief"` = active; anything else marks the vacancy deleted/inactive. |
| `_embedded.custom_fields[name="Aantal uren."]` | MinHours/MaxHours | Optional. `"min-max"` or single value. |
| (derived by ReferralTool) | Vacancy `Url` | `ImportSetting.VacancySiteUrlTemplate.Replace("{vacancyId}", id)`. Point template at the ATS career site. |

# Appendix B -- ReferralTool integration contract: STATUS UPDATE (frozen)

Source of truth at design time:
- `Api/Controllers/KafkaController.cs`
- `Api/Models/KafkaCreateCandidatePayload.cs`

**Request the ATS makes:**
- Method: `POST`
- Route: `candidatestatusupdate` (controller action `[HttpPost("candidatestatusupdate")]`; confirm full
  versioned path, expected `/v1.0/.../candidatestatusupdate`, before Phase 3)
- Auth header: `X-Auth-Token: {token}` -- compared by ReferralTool (direct string equality) against
  config key `Kafka:AuthToken`.
- Body (`KafkaCreateCandidatePayload`):

  | Field | Type | Required | Max len | ATS source |
  |---|---|---|---|---|
  | `CustomerId` | int | yes | - | `TenantSettings.ReferralToolCustomerId` |
  | `Code` | string | yes | 36 | `Application.SourceCode` (referrer `1...` or referral `2...`) |
  | `ExternalVacancyId` | string | yes | 36 | `Job.ExternalRef` |
  | `ExternalCandidateId` | string | yes | 36 | `Candidate.Id` (as string, <=36 chars) |
  | `CandidateStatus` | string | no | - | mapped stage name (Section 8) |

  ```json
  {
    "customerId": 42,
    "code": "1RR123456",
    "externalVacancyId": "JOB-1042",
    "externalCandidateId": "c-5f6897d3",
    "candidateStatus": "1st Interview"
  }
  ```

**ReferralTool-side behavior the ATS depends on:**
- First status for an unknown `ExternalCandidateId` creates the Candidate, resolving the referrer from
  `Code` (prefix `1` = referrer code, prefix `2` = referral code).
- `ExternalVacancyId` must already exist as a ReferralTool Vacancy `ExternalId` (i.e. the job must have
  been imported via Appendix A first).
- Duplicate guard: the same event type on the same candidate is rejected; do not resend an identical stage.
- `CandidateStatus` must match a seeded `CustomerEventType.Type` for that customer (case-insensitive),
  else ReferralTool rejects it.

# Appendix C -- Code prefix + URL parameter rules (frozen)

- Referrer code prefix `"1"`, referral code prefix `"2"`, both 9 chars.
- The career-site referral query parameter name is set by ReferralTool's `Customer.CodeParameterName`
  (default `ref`). The ATS career site must read whatever parameter name the tenant configures and
  store its value verbatim on `Application.SourceCode`.

---

## 15. Portability note (context across repos)

The ATS will live in its own repository/solution; a Claude session there will not have ReferralTool
context. Appendices A-C are the complete, frozen contract. When the ATS repo is scaffolded, copy this
section into `docs/integration/referraltool-contract.md` in the new repo and reference it from the new
project's CLAUDE.md, so future ATS sessions stay accurate without reading ReferralTool source.
