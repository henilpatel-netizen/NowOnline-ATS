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
`HttpTenantContext.CurrentTenantId` reads the `tenant_id` claim first (back-office), then
`HttpContext.Items["TenantId"]`. The item is set only by `TenantResolutionMiddleware`, for public
career-site requests (`/careers/{slug}` -> tenant id). In the `Ats.Worker`, `ITenantContext` is a
settable `WorkerTenantContext` the `OutboxProcessor` sets per message.

## Documented bypasses (the complete list)
- `IdentityService.ValidateCredentialsAsync` — `IgnoreQueryFilters()` at sign-in (no claim yet).
- `OnboardingStore.CreateTenantGraphAsync` — creates the tenant graph and sets `TenantId` by hand on
  settings/template/stages/owner before a claim exists, inside one transaction.
- `OnboardingStore.SlugExistsAsync` / `EmailExistsAsync`: sign-up uniqueness checks with
  `IgnoreQueryFilters()` before a claim exists (email is globally unique across tenants).
- `TenantResolutionMiddleware` — resolves `{slug}` -> Active tenant and sets `Items["TenantId"]`
  (career site). Unknown/suspended slug returns 404.
- `OutboxClaimStore.ClaimDueAsync` (`Ats.Worker`): claims due outbox messages across all tenants
  with one raw-SQL statement (`SqlQueryRaw`, which the LINQ query filter does not apply to); the
  `OutboxProcessor` then sets `WorkerTenantContext.CurrentTenantId` per message.

## Branding (redesign)
`TenantSettings` carries per-tenant branding (`BrandAccentColor`, `BrandSidebarTheme`, career hero
copy). `ITenantBrandingService` resolves them, cached per request, with
NowOnline defaults for nulls. It reads `Tenants` by id (Tenant is not an `ITenantEntity`, so it is
unfiltered) and `TenantSettings` under the normal filter. This introduced **no** new filter-bypass
spot; the five above are still the only ones.

## Rule
Outside those five documented spots: never `IgnoreQueryFilters()`, never hand-set `TenantId`, never
expose an unfiltered queryable. See `.claude/rules/multi-tenancy.md`.
