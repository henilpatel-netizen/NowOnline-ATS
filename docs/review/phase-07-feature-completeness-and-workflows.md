# Phase 7 — Feature Completeness & Workflows

Gaps that block real-world use across the **User, Admin, and Customer** personas. These were confirmed
by direct inspection: the product currently supports a single self-registered Owner per tenant, with
no way to reset a password, invite colleagues, differentiate roles, or notify anyone.

Raises: Feature completeness (and unblocks SEC-4's role model). Verified against `c7f4614`.

Personas:
- **Admin (tenant Owner):** configures the tenant — pipelines, org, integrations, branding, users.
- **User (Recruiter / Hiring Manager / Viewer):** works jobs, candidates, the board.
- **Customer (public applicant):** the career site + apply flow.

---

### [ ] FEAT-1 · Password reset / forgot-password — Priority: High · Effort: M
**Files:** `src/Ats.Web/Controllers/AccountController.cs` — only `Register`, `Login`, `Logout` exist.
**Problem:** A user who forgets their password is permanently locked out; no self-service recovery. A
hard blocker for any real user base.
**Fix:** Add Forgot-password → email a signed, expiring, single-use reset token → Reset-password
(enforcing SEC-5 policy). Requires the email capability (FEAT-4). Store token hashes, not tokens.
Rate-limit the request endpoint (SEC-2).
**Acceptance:** A user can request a reset, receive a link, set a new password, and log in.
**Verify:** Full loop end-to-end with a test mailbox; expired/used tokens are rejected.

---

### [ ] FEAT-2 · User / team management + invitations — Priority: High · Effort: L
**Files:** no `Users`/`Team` controller (`ls src/Ats.Web/Controllers` — none); the only user is the
self-registered Owner (`OnboardingStore.CreateTenantGraphAsync`).
**Problem:** A tenant Owner cannot add colleagues — every tenant is effectively single-user. There is
no invite, list, edit-role, deactivate, or remove-user flow. The Admin persona is incomplete.
**Fix:**
- A `Users` (team) screen (Owner-gated): list tenant users with role + status; invite by email
  (tokenised acceptance → set password); change role; deactivate/reactivate; prevent removing the
  last Owner.
- Reuse `IIdentityService.CreateUserAsync` (already tenant-aware). Stamp `TenantId` via the normal
  interceptor path.
- Ties to SEC-4 (roles must be enforced) and FEAT-4 (invite email).
**Acceptance:** An Owner can invite a Recruiter who accepts, sets a password, logs in, and sees only
their permitted actions.
**Verify:** Invite → accept → login as each role; last-Owner deletion is blocked.

---

### [ ] FEAT-3 · Role-aware UI (hide/disable unpermitted actions) — Priority: High · Effort: M
**Files:** all back-office views with mutating controls (New job/candidate, board move, publish/close,
pipeline edit, org edit, delete buttons).
**Problem:** With SEC-4 enforcing policies server-side, the UI must not present actions the current
role can't perform (a Viewer seeing a "Delete" that 403s is bad UX and confusing).
**Fix:** Gate action rendering on the same policies (`@if (User.HasPermission(...))` via an
authorization service or `IAuthorizationService.AuthorizeAsync` in views). Keep server-side checks as
the source of truth (defence in depth).
**Acceptance:** Each role sees only the controls it can use; hidden controls are also enforced
server-side.
**Verify:** As Viewer, mutating controls are absent and the endpoints 403.

---

### [ ] FEAT-4 · Email / notification capability — Priority: High · Effort: M
**Files:** none — no `IEmailSender`/SMTP/SendGrid implementation exists, despite `CLAUDE.md` /
`Ats.Worker` describing "notifications".
**Problem:** No transactional email at all → blocks FEAT-1 (reset), FEAT-2 (invites), email
verification (SEC-2), and any candidate/recruiter status notifications.
**Fix:** Introduce an `IEmailSender` abstraction (Application) with an SMTP/provider implementation
(Infrastructure), config-driven, with a dev "log/no-op" sender. Send through the outbox/worker for
reliability where appropriate. Templated, localisable messages (FEAT-5).
**Acceptance:** The app can send templated transactional email; dev uses a safe no-op/log sender.
**Verify:** Trigger a reset/invite; the email is produced (captured by the dev sender / test SMTP).

---

### [ ] FEAT-5 · Localisation (NL/EN) — Priority: Medium · Effort: L
**Files:** all UI strings hardcoded; `lang="en"` fixed; a stray Dutch CTA
(`Careers/.../Jobs/Index.cshtml:57`). The product brief is bilingual (NL/EN).
**Problem:** No path to a Dutch UI; mixed-language strings today.
**Fix:** Introduce `IStringLocalizer`/`.resx` resources; set `<html lang>` from the request culture;
add a culture switch (and per-tenant/user default). Localise the career site and transactional emails.
**Acceptance:** The UI renders fully in EN and NL; `lang` matches content.
**Verify:** Switch culture to `nl`; UI + career site render Dutch; `lang="nl"`.

---

### [ ] FEAT-6 · Candidate & application lifecycle edge cases — Priority: Medium · Effort: M
**Files:** `ApplicationService`, `CandidateService`, board/drawer views.
**Problem / gaps to close (verify current behaviour, then fill):**
- Editing/deleting a candidate who has applications (soft-delete cascade + board display).
- Re-applying after rejection; withdrawing an application (status `Withdrawn` exists — is there a UI
  path?).
- Bulk actions on the board (reject/move multiple) — currently one-at-a-time.
- Duplicate-candidate detection beyond exact email (name + phone).
- Notes / comments / rating on an application (a core ATS feature — confirm absent, then scope).
- Resume re-upload / multiple attachments.
**Fix:** Triage each against product priorities; implement the must-haves (withdraw, notes/rating,
edit-with-applications) with tenant-safe queries and audit entries.
**Acceptance:** The recruiter workflow has no dead-ends for the common lifecycle actions.
**Verify:** Walk a candidate from apply → reject → re-apply → note → withdraw without a dead-end.

---

### [ ] FEAT-7 · Tenant lifecycle & self-service admin — Priority: Low · Effort: M
**Files:** `TenantSettings`, onboarding, no billing/plan/suspension UI.
**Problem:** No tenant suspension/offboarding UI (the `TenantStatus.Suspended` enum exists and the
career site 404s a suspended slug — but there's no admin path to suspend), no data-export/delete for
GDPR, no plan/quota concept.
**Fix:** Scope to product needs: a super-admin/tenant-status control, a tenant data export
(GDPR Article 20) and delete (Article 17), and an audit of who suspended/exported.
**Acceptance:** A tenant can be suspended/reactivated and its data exported/deleted on request.
**Verify:** Suspend a tenant → its career site 404s and users can't log in; export produces the
tenant's data.

---

## Exit criteria
- [ ] Password reset, user invitations, and role-aware UI work end-to-end.
- [ ] Transactional email is in place (dev-safe sender); notifications flow.
- [ ] EN/NL localisation available; no mixed-language strings.
- [ ] The must-have candidate lifecycle actions have no dead-ends.
- [ ] Tenant suspension + GDPR export/delete exist.
- [ ] `dotnet build` clean, `dotnet test` green; persona walkthroughs pass.
