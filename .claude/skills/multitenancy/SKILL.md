---
name: multitenancy
description: The Ats tenancy spine — TenantEntity, global query filter, SaveChanges interceptor, tenant context resolution, and the documented filter-bypass spots. Read before touching any tenant-scoped data path.
---

# Ats Multi-Tenancy

## Model
Shared DB + `int TenantId` discriminator. Tenant-scoped entities extend
`Ats.Domain/Common/TenantEntity.cs` (which carries `TenantId` + `KeyedEntity` Id/Key/timestamps) or
implement `ITenantEntity`.

## Enforcement (automatic)
- **Query filter:** `AtsDbContext.OnModelCreating` adds `e.TenantId == GetTenantIdOrZero()` to every
  `ITenantEntity`. `GetTenantIdOrZero()` returns `ITenantContext.CurrentTenantId ?? 0`. Real ids start
  at 1, so "no tenant" filters everything out — fail closed, never leak.
- **Stamping:** `TenantSaveChangesInterceptor` sets `TenantId` on `Added` `ITenantEntity` rows from
  `ITenantContext`, and throws if none is resolved. Also stamps `CreatedAt`/`UpdatedAt`.

## Tenant resolution
`HttpTenantContext.CurrentTenantId` reads the `tenant_id` claim first (back-office/API), then
`HttpContext.Items["TenantId"]`. The item is set by `TenantResolutionMiddleware` for public career-site
requests (`/careers/{slug}` -> tenant id) and by `FeedApiKeyFilter` for vacancy-feed requests. In the
`Ats.Worker`, `ITenantContext` is a settable `WorkerTenantContext` the outbox drainer sets per message.

## Documented bypasses (the complete list)
- `IdentityService.ValidateCredentialsAsync` — `IgnoreQueryFilters()` at sign-in (no claim yet).
- `OnboardingStore.CreateTenantGraphAsync` — creates the tenant graph and sets `TenantId` by hand on
  settings/template/stages/owner before a claim exists, inside one transaction.
- `TenantResolutionMiddleware` — resolves `{slug}` -> Active tenant and sets `Items["TenantId"]`
  (career site). Unknown/suspended slug returns 404.
- `FeedApiKeyFilter` (`Ats.Api`) — resolves the hashed `Authorization: Token` feed key
  (`IgnoreQueryFilters` over `TenantSettings`) and sets `Items["TenantId"]`.
- `OutboxClaimStore.ClaimDueAsync` (`Ats.Worker`) — claims Pending outbox messages across all tenants
  with `IgnoreQueryFilters`; the drainer then sets `WorkerTenantContext.TenantId` per message.

## Branding (redesign)
`TenantSettings` carries per-tenant branding (`BrandAccentColor`, `BrandSidebarTheme`, career hero
copy) and `FeedLastPulledAt`. `ITenantBrandingService` resolves them, cached per request, with
NowOnline defaults for nulls. It reads `Tenants` by id (Tenant is not an `ITenantEntity`, so it is
unfiltered) and `TenantSettings` under the normal filter. This introduced **no** new filter-bypass
spot; the five below are still the only ones.

## Rule
Outside those five documented spots: never `IgnoreQueryFilters()`, never hand-set `TenantId`, never
expose an unfiltered queryable. See `.claude/rules/multi-tenancy.md`.
