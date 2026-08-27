# Phase 2 — Security & Access Control

The tenant-isolation and authz **core is sound** (fail-closed global filter, SaveChanges interceptor,
no IDOR, antiforgery global, PBKDF2 hashing) — do not rework it. These items close the remaining
gaps: secrets at rest, account-abuse protection, a real role model, and defence-in-depth headers.

Raises: Security. `SEC-1` depends on `FND-1` (persisted Data Protection keys). Verified against `c7f4614`.

> **Status (2026-07-31):** By product decision, only **SEC-7** is implemented in this pass ("one email
> globally" model). SEC-1 to SEC-6, SEC-8, SEC-9 are deferred to a future pass.

---

### [ ] SEC-1 · Encrypt ReferralTool secrets at rest — Priority: Critical · Effort: M
**Files:** `src/Ats.Domain/Entities/TenantSettings.cs:10,15` (`ReferralToolAuthToken`,
`ReferralToolApiKey`), `src/Ats.Infrastructure/Integration/IntegrationSettingsService.cs:28-31`,
`.../Configurations/TenantSettingsConfiguration.cs` (no value converter). **Confirmed live:** the raw
token/key are readable directly in the DB.
**Problem:** Every tenant's third-party credentials sit in plaintext in a shared DB — one backup leak,
snapshot, read-only insider, or SQLi elsewhere exposes all of them at once (OWASP A02).
**Fix:**
- Add an EF Core value converter that encrypts on write / decrypts on read, backed by ASP.NET Data
  Protection (`IDataProtector` with a dedicated purpose) — requires FND-1 for stable keys — or SQL
  Always Encrypted / Key Vault.
- Apply it to both columns in `TenantSettingsConfiguration`. Keep the masked-write UI (only
  `HasAuthToken`/`HasApiKey` booleans are exposed today — good).
- Migration: the columns hold ciphertext now; provide a one-time re-encrypt of existing rows or force
  re-entry.
**Acceptance:** Raw DB columns contain ciphertext; the app reads/uses the plaintext transparently;
the feed test connection still succeeds.
**Verify:** `SELECT LEFT(ReferralToolAuthToken,24)…` shows ciphertext, not the readable token; the
Integration "Test connection" still works.

---

### [ ] SEC-2 · Login rate-limiting + account lockout — Priority: High · Effort: M
**Files:** `src/Ats.Web/Controllers/AccountController.cs:44` (Login), identity
`src/Ats.Infrastructure/Identity/IdentityService.cs`.
**Problem:** No throttling or lockout on login → credential stuffing / brute force (OWASP A07).
**Fix:**
- Add .NET `RateLimiter` middleware (fixed/sliding window) scoped to `/Account/Login` (and Register),
  keyed by IP + email.
- Add a failed-attempt counter + lockout window on `AppUser` (e.g. `AccessFailedCount`,
  `LockoutEndUtc`), enforced in `ValidateCredentialsAsync`. One additive migration.
**Acceptance:** N failed logins within the window are blocked; lockout releases after the window.
**Verify:** Script >N bad logins → 429 / lockout message; correct login after cooldown succeeds.

---

### [ ] SEC-3 · Global authorization fallback policy — Priority: High · Effort: S
**Files:** `src/Ats.Web/Program.cs:27` (`AddAuthorization()` with no fallback).
**Problem:** Protection depends on each controller remembering `[Authorize]`. A future controller
added without it is silently public (OWASP A01).
**Fix:** Set
`options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();` and
mark the public Careers area (already `[AllowAnonymous]`), Account login/register, and the feed API
`[AllowAnonymous]` explicitly.
**Acceptance:** A new controller with no attribute requires auth by default; public endpoints still work.
**Verify:** Temporarily add an un-attributed test action → it 302s to login; remove it.

---

### [ ] SEC-4 · Enforce a real role model (Recruiter / HiringManager / Viewer) — Priority: High · Effort: M
**Files:** `src/Ats.Domain/Enums/AtsRole.cs` (4 roles defined), but **only `Owner` is ever checked**
(`AuditController.cs:8`, `IntegrationController.cs:10`, `CareerSiteController.cs:20,37`). Confirmed:
Recruiter/HiringManager/Viewer are referenced nowhere in authorization.
**Problem:** A "Viewer" (read-only intent) can create/edit/delete jobs, candidates, pipelines,
applications — the privilege model is defined but unimplemented. Real security + workflow gap.
**Fix:**
- Define named authorization policies (e.g. `CanManageJobs`, `CanMoveApplications`, `CanConfigure`,
  `ReadOnly`) mapping to roles, registered in `AddAuthorization`.
- Apply `[Authorize(Policy=…)]` to mutating actions across Jobs/Candidates/Board/Pipelines/Org.
- Hide/disable actions in views the current role can't perform (ties to FEAT-3).
- Document the role→permission matrix.
**Acceptance:** A Viewer cannot reach mutating endpoints (403) and doesn't see their buttons; Owner/
Recruiter/HiringManager have the intended access.
**Verify:** Sign in as each role (needs FEAT-2 user mgmt, or seed users) and confirm the matrix.

---

### [ ] SEC-5 · Strong password policy — Priority: Medium · Effort: S
**Files:** `src/Ats.Web/Models/RegisterViewModel.cs:11` (`[Required]` only).
**Problem:** 1-character owner passwords are allowed (hashing itself is fine — PBKDF2).
**Fix:** Add min length ≥12 and a complexity or breached-password check (NIST/OWASP ASVS aligned);
mirror on any password-set path (ties to FEAT-1 reset).
**Acceptance:** Weak passwords are rejected server-side with a clear message.
**Verify:** Register with `a` → rejected; strong password → accepted.

---

### [ ] SEC-6 · Validate ReferralTool base URL — Priority: Medium · Effort: S
**Files:** `src/Ats.Infrastructure/Integration/IntegrationSettingsService.cs:23`,
`.../Integration/ReferralToolClient.cs` (uses the URL verbatim).
**Problem:** An `http://` value sends the API key/token in cleartext; an internal/loopback URL is a
(owner-scoped) SSRF surface.
**Fix:** Require `https://`, reject private/loopback/link-local hosts, optionally allowlist the
ReferralTool domain. Validate on save.
**Acceptance:** Non-https or private-host base URLs are rejected on save.
**Verify:** Save `http://…` → rejected; save `https://api.referraltool.nl` → accepted.

---

### [x] SEC-7 · Cross-tenant email identity model — Priority: Medium · Effort: M
**Done (2026-07-31), model chosen: globally-unique email + single membership.**
- `AppUserConfiguration`: unique index changed from `(TenantId, Email)` to global `IX_Users_Email`.
- Migration `MakeUserEmailGloballyUnique` (drops the composite, adds the single-column unique index) — apply manually.
- `IdentityService.ValidateCredentialsAsync`: now `SingleOrDefaultAsync` on email (deterministic; at most one user).
- `TenantOnboardingService.RegisterAsync`: rejects an already-registered email (new `IOnboardingStore.EmailExistsAsync`) before insert.
- Tests: `TenantOnboardingServiceTests` (4). Pre-check confirmed no existing cross-tenant duplicate emails.

**Files:** `src/Ats.Infrastructure/Identity/IdentityService.cs:43` (`IgnoreQueryFilters()
.FirstOrDefaultAsync(u => u.Email == normalized)`); unique index is `(TenantId, Email)` only.
**Problem:** The same email in two tenants authenticates against whichever row returns first →
wrong-tenant routing / lockout; ambiguous identity.
**Fix:** Decide the model: either (a) globally-unique email + single membership, or (b) keep
per-tenant emails but add a tenant-selection step at login when an email matches multiple tenants.
Add the appropriate unique index.
**Acceptance:** Login is deterministic and routes to the correct tenant in all cases.
**Verify:** Create the same email in two tenants; confirm the chosen model behaves as designed.

---

### [ ] SEC-8 · `no-store` on authenticated responses — Priority: Medium · Effort: S
**Files:** `src/Ats.Web/Views/Shared/_Layout.cshtml` / `Program.cs` (no cache-control on authed pages).
**Problem:** After logout, the browser Back button can show cached tenant pages (privacy issue on
shared machines).
**Fix:** Add a global response header for authenticated pages:
`Cache-Control: no-store, no-cache, must-revalidate` (a result filter or middleware for
non-`AllowAnonymous` responses). Leave static assets and public careers pages cacheable.
**Acceptance:** Back button after logout does not reveal tenant data.
**Verify:** Log in, browse, log out, press Back → redirected to login, not the cached page.

---

### [ ] SEC-9 · Security headers, CSP, and upload magic-byte check — Priority: Low · Effort: S
**Files:** `src/Ats.Web/Program.cs` (no headers/CSP); upload validation
`src/Ats.Web/Areas/Careers/Controllers/JobsController.cs:160-167`; branding inline `<style>`
`Views/Shared/Components/Branding/Default.cshtml:29`.
**Problem:** No `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`. Resume
upload trusts client-supplied extension + content-type (no signature sniff). (No XSS found — branding
style is built from re-validated hex only, Razor auto-encodes.)
**Fix:** Add a headers middleware with a baseline CSP (nonce/hash for the branding inline style),
`nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options`/frame-ancestors. Add
a magic-byte sniff (PDF `%PDF`, DOCX zip signature) to the resume validator.
**Acceptance:** Response carries the headers; a renamed non-PDF is rejected on upload.
**Verify:** Inspect response headers; upload a `.pdf` that is actually a text file → rejected.

---

## Exit criteria
- [ ] Integration secrets are encrypted at rest (ciphertext in DB); feed test still works.
- [ ] Login is rate-limited + lockout-protected; passwords meet policy.
- [ ] Fallback authz policy on; every mutating action is role-gated per the documented matrix.
- [ ] Base-URL validated; email identity deterministic; authed pages `no-store`.
- [ ] Security headers/CSP present; upload signature-checked.
- [ ] `dotnet build` clean, `dotnet test` green.
