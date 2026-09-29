# Multi-Tenancy Rules (CRITICAL)

- Tenant identity is `int TenantId`. Every tenant-scoped entity extends `TenantEntity`
  (`Ats.Domain/Common/TenantEntity.cs`) or implements `ITenantEntity`.
- Isolation is automatic:
  - `AtsDbContext.OnModelCreating` applies a global query filter `e.TenantId == GetTenantIdOrZero()`
    to every `ITenantEntity`. No tenant in context => returns 0 => queries return nothing (fail closed).
  - `TenantSaveChangesInterceptor` stamps `TenantId` on insert from `ITenantContext`. Inserting a
    tenant entity with no tenant in context throws.
- Do NOT hand-set `TenantId` in normal code; let the interceptor do it.
- There are exactly five documented places that bypass the filter (`IgnoreQueryFilters()`), set
  `TenantId` by hand, or set `HttpContext.Items["TenantId"]`: the three below, plus the career-site
  slug middleware and the outbox worker claim described after them. Outside these, never do any of those:
  - `IdentityService.ValidateCredentialsAsync` (sign-in: no tenant claim yet; matches unique `(TenantId, Email)`).
  - `OnboardingStore.CreateTenantGraphAsync` (creates the tenant graph before a tenant claim exists;
    stamps `TenantId` explicitly on settings/template/stages/owner).
  - `OnboardingStore.SlugExistsAsync` / `EmailExistsAsync` (sign-up uniqueness checks run before a
    tenant claim exists; email is globally unique across tenants, so both read unfiltered).
- Public career-site requests resolve the tenant from the `{slug}` route value:
  `TenantResolutionMiddleware` sets `HttpContext.Items["TenantId"]` and `HttpTenantContext` reads it
  after the `tenant_id` claim. Unknown/suspended slug returns 404. Querying `Tenants` by slug is
  unfiltered (Tenant is not an `ITenantEntity`). This is the only place `Items["TenantId"]` is set.
- The outbox worker (`Ats.Worker`) drains `OutboxMessages` across all tenants via a raw-SQL atomic
  claim (`OutboxClaimStore`, `UPDATE ... WITH (READPAST, UPDLOCK, ROWLOCK) ... OUTPUT`, which bypasses
  the LINQ query filter by construction), then sets a settable `WorkerTenantContext.CurrentTenantId` to each message's
  `TenantId` before processing, so per-tenant reads, `TenantId` stamping, and the `WebhookDelivery`
  insert scope correctly. The worker registers `WorkerTenantContext` in place of `HttpTenantContext`
  (no HttpContext). This is a documented filter-bypass spot.
- Never expose a queryable that bypasses the filter outside those documented spots.
