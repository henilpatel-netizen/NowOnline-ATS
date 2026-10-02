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
`HttpTenantContext.CurrentTenantId` reads `HttpContext.Items["TenantId"]` first, then the `tenant_id`
claim (back office). The item is set only by `TenantResolutionMiddleware`, for career-site requests
(`/careers/{slug}` -> tenant id), so it takes precedence over the claim: career-site requests resolve
from the slug for everyone, including a signed-in user of another tenant. Only the Careers area carries
a `slug` route value. In the `Ats.Worker`, `ITenantContext` is a
settable `WorkerTenantContext` the `OutboxProcessor` sets per message.

## Documented bypasses (the complete list)
- `IdentityService.ValidateCredentialsAsync` (`IgnoreQueryFilters()` at sign-in, no claim yet; matches the
  unique email) and `IdentityService.GetSessionAsync` (cookie validation and cookie re-issue: per-request
  validation runs before `HttpContext.User`, so before the tenant context, and `ProfileController`
  re-issues the cookie after a password change; both are scoped by the same cookie values, filtered
  explicitly by the cookie's `tenant_id` AND the user id). One place, two methods.
- `OnboardingStore.CreateTenantGraphAsync` — creates the tenant graph and sets `TenantId` by hand on
  settings/template/stages/owner before a claim exists, inside one transaction.
- `OnboardingStore.SlugExistsAsync` / `EmailExistsAsync`: sign-up uniqueness checks with
  `IgnoreQueryFilters()` before a claim exists (email is globally unique across tenants).
  `EmailExistsAsync` is also called by the back-office `UserService.CreateAsync` / `UpdateAsync` for the same
  global uniqueness check. It returns only a boolean, so an Owner can learn that an address is registered
  somewhere; accepted, because the `IX_Users_Email` unique index implies it. No new bypass spot.
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

## Job scoping is not tenancy
HiringManager job scoping (authorization skill) is a separate, explicit filter (`JobScopeFilter`, `IJobScope`). It
never uses a global query filter, and it always joins through `db.Jobs` so the soft-delete and tenant filters apply.
It adds no filter-bypass spot.

## Rule
Outside those five documented spots: never `IgnoreQueryFilters()`, never hand-set `TenantId`, never
expose an unfiltered queryable. See `.claude/rules/multi-tenancy.md`.
