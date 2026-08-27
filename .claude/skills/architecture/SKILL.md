---
name: architecture
description: Ats solution layout, strict layering, DI registration, and where each kind of code belongs. Read before exploring the codebase.
---

# Ats Architecture

## Solution
`Ats.slnx` (modern XML solution format, .NET 10 default), projects under `src/`:
- `Ats.Domain` — entities, enums, domain rules. No dependencies.
- `Ats.Application` — use-case services + abstractions (`ITenantContext`, `ICurrentUser`,
  `IIdentityService`, onboarding) + validators. References Domain.
- `Ats.Infrastructure` — `AtsDbContext`, EF configurations, `TenantSaveChangesInterceptor`,
  `HttpTenantContext`, `IdentityService`, `OnboardingStore`, `DependencyInjection`. References Application.
- `Ats.Web` — MVC back-office at the root (`/Jobs`, `/Candidates`, `/Board`, `/Integration`, ...); cookie
  auth; controllers are thin. The public career site is the `Careers` area at `/careers/{slug}`.
- `Ats.Api` — REST API (feed + integration; built out in Phase 3).
- `Ats.Worker` — background host (outbox delivery; built out in Phase 3).

## Layering (strict)
Controllers -> Application services -> repositories -> EF Core. Domain has no framework deps.
Controllers hold no business logic. Always async/await.

## DI
Infrastructure wiring is centralised in `Ats.Infrastructure/DependencyInjection.cs`
(`AddAtsInfrastructure`). Register new services there. Hosts call `AddAtsInfrastructure(config)` in
`Program.cs`.

## Where things go
- New entity -> `Ats.Domain/Entities` (+ `IEntityTypeConfiguration` in
  `Ats.Infrastructure/Persistence/Configurations`).
- New use case -> an Application service behind an interface; impl/store in Infrastructure.
- New cross-cutting capability -> abstraction in `Ats.Application/Abstractions`, impl in Infrastructure.
- New back-office page or view -> follow `.claude/skills/ui/SKILL.md` (layouts, tokens, components).

## Conventions
- Cookie auth for back-office; JWT for Api (Phase 3+). Auth always behind `IIdentityService`.
- Timestamps via `KeyedEntity.CreatedAt/UpdatedAt`, stamped by the interceptor, stored UTC.

## Data-access conventions (Phase 3/4)
- **Do NOT switch to `AddDbContextPool`.** It evaluates the options delegate once, which would capture
  the scoped `ITenantContext` held by `TenantSaveChangesInterceptor` and stamp every tenant's inserts
  with the first resolved tenant id — a cross-tenant corruption against the CRITICAL tenancy rule.
  Pooling needs a pool-safe tenant-resolution redesign first (see phase-04 status note).
- `EnableRetryOnFailure` is on. Any **explicit** transaction must therefore run inside the execution
  strategy. Use `IApplicationRepository.InTransactionAsync(...)` (or
  `Database.CreateExecutionStrategy().ExecuteAsync(...)`) — never bare `BeginTransactionAsync`.
- Multi-step writes that must be all-or-nothing (application + first event + outbox message) go through
  that helper; see `ApplicationService.CreateApplicationAsync` and `CareerService.ApplyAsync`.
- Pure reads that return entities use `AsNoTracking()`. Projection query services already do.
- Aggregate **in SQL**, not in memory: group counts server-side and bound correlated lookups with
  `Take(n)`. `JobListQuery` is the reference (stage tallies grouped in SQL, avatar names via `TOP 3`),
  so per-page cost does not grow with how many people applied.
- No server-side data cache exists by design (freshness requirement) — see the phase-04 status note
  before introducing one; anything cached must be invalidated for the user's own writes, and
  `OutboxMessage` state is also written by `Ats.Worker`, i.e. out of process.
