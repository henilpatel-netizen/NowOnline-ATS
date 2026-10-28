# Multi-Tenancy Rules (CRITICAL)

- Tenant identity is `int TenantId`. Every tenant-scoped entity extends `TenantEntity`
  (`Ats.Domain/Common/TenantEntity.cs`) or implements `ITenantEntity`.
- Isolation is automatic:
  - `AtsDbContext.OnModelCreating` applies a global query filter `e.TenantId == GetTenantIdOrZero()`
    to every `ITenantEntity`. No tenant in context => returns 0 => queries return nothing (fail closed).
  - `TenantSaveChangesInterceptor` stamps `TenantId` on insert from `ITenantContext`. Inserting a
    tenant entity with no tenant in context throws.
- Do NOT hand-set `TenantId` in normal code; let the interceptor do it.
- There are exactly six documented places that bypass the filter (`IgnoreQueryFilters()`), set
  `TenantId` by hand, or set `HttpContext.Items["TenantId"]`: the three below, plus the career-site
  slug middleware, the outbox worker claim and the account store described after them. Outside these, never do any of those:
  - `IdentityService.ValidateCredentialsAsync` (sign-in: no tenant claim yet; matches the unique email) and
    `IdentityService.GetSessionAsync` (cookie validation and cookie re-issue: per-request validation runs
    before `HttpContext.User`, so before the tenant context, and `ProfileController` re-issues the cookie
    after a password change; both are scoped by the same cookie values, filtered explicitly by the
    cookie's `tenant_id` AND the user id).
  - `OnboardingStore.CreateTenantGraphAsync` (creates the tenant graph before a tenant claim exists;
    stamps `TenantId` explicitly on settings/template/stages/owner).
  - `OnboardingStore.SlugExistsAsync` / `EmailExistsAsync` (sign-up uniqueness checks run before a
    tenant claim exists; email is globally unique across tenants, so both read unfiltered).
    `EmailExistsAsync` is also called by the back-office `UserService.CreateAsync` and `UpdateAsync` for the
    same global uniqueness check. It returns only a boolean, so an Owner can learn that an address is
    registered somewhere; accepted, because the `IX_Users_Email` unique index implies it anyway. This adds no
    new bypass spot.
- Career-site requests resolve the tenant from the `{slug}` route value for everyone, signed in or not:
  `TenantResolutionMiddleware` sets `HttpContext.Items["TenantId"]`, and in `HttpTenantContext` that
  item (set only by the slug middleware) takes precedence over the `tenant_id` claim. Only the Careers
  area carries a `slug` route value. Unknown/suspended slug returns 404 for everyone. Querying `Tenants` by slug is
  unfiltered (Tenant is not an `ITenantEntity`). This is the only place `Items["TenantId"]` is set.
- The outbox worker (`Ats.Worker`) drains `OutboxMessages` across all tenants via a raw-SQL atomic
  claim (`OutboxClaimStore`, `UPDATE ... WITH (READPAST, UPDLOCK, ROWLOCK) ... OUTPUT`, which bypasses
  the LINQ query filter by construction), then sets a settable `WorkerTenantContext.CurrentTenantId` to each message's
  `TenantId` before processing, so per-tenant reads, `TenantId` stamping, and the `WebhookDelivery`
  insert scope correctly. The worker registers `WorkerTenantContext` in place of `HttpTenantContext`
  (no HttpContext). This is a documented filter-bypass spot.
- `IAccountStore` (`Ats.Application/Accounts`, impl `Ats.Infrastructure/Identity/AccountStore`) serves the
  signed-out account links (forgot password, invite, email change), where no tenant is in context. It reads
  `Users` with `IgnoreQueryFilters()` only by the globally unique normalised email (`FindByEmailAsync`, like
  `ValidateCredentialsAsync`) or by an explicit tenant id AND user id (`GetAsync`); callers pass only ids from a
  token validated by `IAccountTokens` (which carries the user's `SecurityStamp`) or the email. `Tenants` reads are
  unfiltered by nature; `EmailTakenAsync` delegates to `OnboardingStore.EmailExistsAsync`. `Audit` adds an
  `AuditEntry` with `TenantId` set explicitly (the interceptor leaves a non-zero `TenantId` alone), so it is also
  a documented hand-set `TenantId` spot (it takes the tenant, user id and name from the loaded `AppUser`). It
  exposes no queryable. Only the account-link services in `Ats.Application/Accounts` may inject `IAccountStore`;
  before acting on a token they must compare `token.SecurityStamp` with the loaded user's stamp and check
  `TenantActiveAsync`. `TrySaveAsync` saves and returns false on the `IX_Users_Email` unique-index violation
  (clearing the change tracker); it adds no bypass.
- `EmailMessages` is a global table like `Tenants`: not an `ITenantEntity`, so there is no filter to
  bypass (its `TenantId` is informational only). It is written only through `IEmailOutbox` and read only
  by the Worker; never expose it to a back-office query. This is not a bypass spot.
- Never expose a queryable that bypasses the filter outside those documented spots.
