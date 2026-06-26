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
`HttpTenantContext` reads the `tenant_id` claim (back-office/API). The career-site slug path
(`/careers/{tenantSlug}` -> tenant id) is added in Phase 2.

## Documented bypasses (the ONLY ones)
- `IdentityService.ValidateCredentialsAsync` — `IgnoreQueryFilters()` at sign-in (no claim yet).
- `OnboardingStore.CreateTenantGraphAsync` — creates the tenant graph and sets `TenantId` by hand on
  settings/template/stages/owner before a claim exists, inside one transaction.

## Rule
Outside those two spots: never `IgnoreQueryFilters()`, never hand-set `TenantId`, never expose an
unfiltered queryable. See `.claude/rules/multi-tenancy.md`.
