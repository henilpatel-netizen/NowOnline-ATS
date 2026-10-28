# Email and Notifications Implementation Plan

> **For agentic workers:** run through `/ats-ship docs/plans/2026-10-28-email-notifications.md`.
> Project overrides apply: implementers never commit or stage; they leave a working-tree diff.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Status (28 October 2026):** Tasks 1 to 8 done and verified, review follow-ups done (see "Review
follow-ups" at the end), Task 9 documentation written. Open follow-ups are listed at the end.

**Goal:** Ats sends transactional email: forgotten password, invite by email, Owner-triggered reset
link, verified email change, security notices, and hiring-team notifications for new applications and
stage moves.

**Architecture:** Web and Application code never talk to SMTP. They stage a rendered `EmailMessage`
row in the same unit of work as the change that caused it (a global, non-tenant queue table). The
Worker claims due rows with the same atomic raw-SQL pattern as `OutboxClaimStore`, sends them with
MailKit (or writes `.eml` files to a pickup directory in tests), retries transient failures with
backoff, and clears the body once a row is finished. Account links (reset, invite, email change) are
stateless Data Protection tokens that carry the user's `SecurityStamp`, so any security change, and
using the link itself, invalidates them; no token table.

**Tech Stack:** .NET 10 MVC, EF Core (SQL Server), MailKit, ASP.NET Core Data Protection
(`ITimeLimitedDataProtector`), ASP.NET Core rate limiting, `.resx` resources, xUnit, Playwright.

**Source:** decisions of 28 October 2026 (developer accepted the recommended defaults D1 to D4) and
`docs/review/phase-07-feature-completeness-and-workflows.md` FEAT-1, FEAT-2, FEAT-4; product spec
`docs/specs/2026-06-26-ats-product-design.md` lines 210 and 266; RBAC spec "Email invites follow when
email is integrated".

## Decisions (28 October 2026)

| # | Decision |
|---|---|
| D1 | Invites and reset links **replace** Owner-typed temporary passwords. `MustChangePassword` and `RequirePasswordChangeFilter` stay for users who still hold one; new code never sets it. |
| D2 | Stage-change email goes to the hiring team only, never to candidates. |
| D3 | Scope: M0 to M7. Applicant confirmation (M8), "assigned as hiring manager" (M9) and sign-up email verification (M10) are follow-ups. |
| D4 | Every email has EN and NL `.resx` texts; rendered in `en` until FEAT-5 adds culture selection. |

## Global Constraints
- No secret in source control or chat. SMTP user name, app password, `FromAddress` and `RedirectAllTo`
  live in `dotnet user-secrets` on `Ats.Worker` (it already has a `UserSecretsId`). Placeholders only in docs.
- Development uses Gmail: `smtp.gmail.com`, port `587`, `StartTls`. Gmail sends as the authenticated
  account, so `FromAddress` must equal `Smtp:UserName`. Quota about 500 messages a day.
- `Email:RedirectAllTo` is allowed **only** in Development (start-up validation fails otherwise). When set,
  every message goes to that address and the subject is prefixed `[to: <original>] `.
- Account links are built from `Integration:CareerSiteBaseUrl` (already validated at start-up; one host
  per environment). **Never** from the request's `Host` header.
- Token lifetimes: password reset 1 hour, invite 7 days, email change 24 hours.
- A token is consumed only by a **POST**. A GET of a link only renders a form (mail scanners prefetch links).
- Emails carry no candidate email, phone or CV; name, job title and a back-office link only.
- One message per recipient (no shared To/CC lists).
- UTC everywhere; build warning-clean; `dotnet format --verify-no-changes` clean; business rules unit-tested
  with hand-rolled fakes; permission-based authorization; `hx-confirm` for confirmations.
- MailKit: pin an exact current stable 4.x version in `Ats.Infrastructure.csproj`; `dotnet list package
  --vulnerable` must report nothing for it.

## Review Focus
1. **Host header poisoning:** a forged `Host` header on Forgot password must not change the link host. Test in Task 4.
2. **Account enumeration:** unknown, inactive, invite-pending and suspended-tenant emails get the same
   response as a known one, and no email. Test in Task 4.
3. **Link prefetch:** GET of a reset/invite/confirm link changes nothing; a second POST with the same
   token fails (stamp rotated). Tests in Tasks 4, 5, 7.
4. **Secrets at rest:** a Delivered or Failed `EmailMessage` has `HtmlBody`/`TextBody` cleared, so no
   live token sits in the database after sending. Test in Task 2.
5. **Gmail failures:** 4xx SMTP replies, connection and auth errors retry with backoff (the daily quota
   answers 4xx/421); 5xx per-recipient rejections fail at once without retrying. Test in Task 2.

---

### Task 1: Email queue (entity, staging, migration)

**Files:**
- Create: `src/Ats.Domain/Entities/EmailMessage.cs`
- Create: `src/Ats.Application/Email/EmailDraft.cs`, `src/Ats.Application/Email/IEmailOutbox.cs`
- Create: `src/Ats.Infrastructure/Email/EmailOutbox.cs`
- Create: `src/Ats.Infrastructure/Persistence/Configurations/EmailMessageConfiguration.cs`
- Modify: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs` (DbSet), `src/Ats.Infrastructure/DependencyInjection.cs`
- Migration: `AddEmailMessages`
- Test: `tests/Ats.Tests/Email/EmailDraftTests.cs`

**Interfaces (produces):**
```csharp
// Not an ITenantEntity: staged from anonymous requests (forgot password) and read only by the Worker.
public class EmailMessage
{
    public int Id { get; set; }
    public int? TenantId { get; set; }          // informational (logs); never a filter
    public string To { get; set; } = "";        // max 256
    public string Subject { get; set; } = "";   // max 300
    public string? HtmlBody { get; set; }       // nvarchar(max); cleared when finished
    public string? TextBody { get; set; }       // nvarchar(max); cleared when finished
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;   // reuse the existing enum
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }      // max 1000, never contains the body
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed record EmailDraft(string To, string Subject, string HtmlBody, string TextBody);
public interface IEmailOutbox
{
    void Stage(EmailDraft draft, int? tenantId);                     // saved by the caller's next SaveChanges
    Task SendAsync(EmailDraft draft, int? tenantId, CancellationToken ct = default);  // Stage + save
}
```
Index: `(Status, NextAttemptAt)`.

- [x] **Step 1:** Test `EmailDraft` guard: `EmailOutbox.Stage` rejects an empty `To` or `Subject` with
  `ArgumentException` (test through a fake-free static `EmailDraft.Validate(draft)` that `Stage` calls).
- [x] **Step 2:** Implement entity, configuration, `EmailOutbox` (adds to `AtsDbContext.EmailMessages`
  with `NextAttemptAt = UtcNow`), DI registration.
- [x] **Step 3:** `dotnet ef migrations add AddEmailMessages --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`; check the migration creates one table and one index only.
- [x] **Step 4:** Document in `.claude/rules/multi-tenancy.md`: `EmailMessages` is a global table like
  `Tenants` (not `ITenantEntity`, no filter to bypass), written through `IEmailOutbox` and read only by
  the Worker; never exposed to a back-office query.
- [x] **Step 5:** build, test, format.

Model: opus (entity, migration). Playwright: none.

---

### Task 2: Worker delivery with MailKit

**Files:**
- Create: `src/Ats.Application/Email/EmailOptions.cs`, `src/Ats.Application/Email/EmailRules.cs`
- Create: `src/Ats.Infrastructure/Email/IEmailTransport.cs`, `MailKitEmailTransport.cs`, `EmailClaimStore.cs`, `EmailProcessor.cs`
- Create: `src/Ats.Worker/EmailDrainer.cs`
- Modify: `src/Ats.Infrastructure/Ats.Infrastructure.csproj` (MailKit), `src/Ats.Worker/Program.cs`,
  `src/Ats.Worker/appsettings.json`, `src/Ats.Worker/appsettings.Development.json`
- Test: `tests/Ats.Tests/Email/EmailRulesTests.cs`, `tests/Ats.Tests/Email/EmailOptionsTests.cs`

**Interfaces:**
- Consumes: Task 1 `EmailMessage`, `OutboxStatus`.
- Produces:
```csharp
public sealed class EmailOptions   // section "Email"
{
    public int PollSeconds { get; set; } = 10;  public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 8;   public int BaseBackoffSeconds { get; set; } = 30;
    public int MaxBackoffSeconds { get; set; } = 3600;  public int ClaimLeaseSeconds { get; set; } = 120;
    public SmtpSettings Smtp { get; set; } = new();
    public string FromAddress { get; set; } = "";  public string FromName { get; set; } = "Ats";
    public string? RedirectAllTo { get; set; }    // Development only
    public string? PickupDirectory { get; set; }  // when set, write .eml files instead of SMTP
    public static IEnumerable<string> Validate(EmailOptions o, bool isDevelopment); // error messages
}
public sealed class SmtpSettings { string Host; int Port = 587; string Security = "StartTls"; string UserName; string Password; }
public enum EmailSendOutcome { Sent, Transient, Permanent }
public static class EmailRules
{
    // SmtpCommandException status: 5xx -> Permanent; 4xx -> Transient. Connection, protocol, auth,
    // IO, timeout -> Transient.
    public static EmailSendOutcome Classify(int? smtpStatusCode, bool connectionOrAuthFailure);
    public static (string To, string Subject) Route(string to, string subject, string? redirectAllTo);
    public static double BackoffSeconds(int attempts, EmailOptions o);  // min(Max, Base * 2^attempts)
}
public interface IEmailTransport { Task SendAsync(string to, string subject, string html, string text, CancellationToken ct); }
```
`Validate` errors: `RedirectAllTo` set outside Development; neither `PickupDirectory` nor `Smtp:Host`
set; `FromAddress` missing; `Security` not one of `None`, `StartTls`, `SslOnConnect`.

- [x] **Step 1: Failing tests** in `EmailRulesTests`: `Classify(550,false)==Permanent`, `Classify(421,false)==Transient`,
  `Classify(452,false)==Transient`, `Classify(null,true)==Transient`; `Route("a@x.nl","Hi",null)==("a@x.nl","Hi")`;
  `Route("a@x.nl","Hi","dev@x.nl")==("dev@x.nl","[to: a@x.nl] Hi")`; `BackoffSeconds` caps at `MaxBackoffSeconds`.
  `EmailOptionsTests`: redirect outside Development is an error; in Development it is not; missing host
  and pickup is an error.
- [x] **Step 2:** Run, see them fail. Implement `EmailOptions`, `EmailRules`. Run, see them pass.
- [x] **Step 3:** `MailKitEmailTransport`: SMTP via `MailKit.Net.Smtp.SmtpClient` (`SecureSocketOptions` from
  `Security`), multipart/alternative text + HTML; when `PickupDirectory` is set, write
  `MimeMessage.WriteTo` to `{PickupDirectory}/{UtcNow:yyyyMMddHHmmssfff}-{Guid}.eml` instead.
- [x] **Step 4:** `EmailClaimStore.ClaimDueAsync(int max, DateTimeOffset now, ct)`: the `OutboxClaimStore`
  pattern without the ordering `NOT EXISTS` (emails are independent): `UPDATE TOP` CTE with
  `READPAST, UPDLOCK, ROWLOCK`, Pending or lease-expired Processing, `OUTPUT inserted.Id, inserted.NextAttemptAt AS Lease`.
- [x] **Step 5:** `EmailProcessor.ProcessAsync(claim)`: load by id, check the lease is still ours, `Route`,
  send, then Sent -> `Delivered`; Permanent -> `Failed`; Transient -> `Attempts++`, backoff, `Failed` at
  `MaxAttempts`. On Delivered and Failed set `HtmlBody = TextBody = null` (Review Focus 4). `LastError`
  holds the SMTP status and exception type only, never the body or the password. Log `MessageId` and
  `TenantId`, never the recipient address or body.
- [x] **Step 6:** `EmailDrainer` (copy `OutboxDrainer`'s loop and per-message isolation) and wiring in
  `Worker/Program.cs`: bind `EmailOptions`, `.Validate` via `EmailOptions.Validate(o, env.IsDevelopment())`,
  `.ValidateOnStart()`. `appsettings.json`: the numeric defaults. `appsettings.Development.json`:
  `Email:Smtp:Host = smtp.gmail.com`, `Port 587`, `Security StartTls`, `FromName "Ats (dev)"`; nothing secret.
- [x] **Step 7:** build, test, format. Manual check by the lead (not the implementer): with user-secrets
  set, stage one row through a unit-of-work in a scratch run or wait for Task 4, and confirm it arrives
  at `RedirectAllTo`.

Model: opus (Worker, retry). Playwright: none. Also review with `silent-failure-hunter`.

**Developer manual step (print in close-out):**
```
dotnet user-secrets set "Email:Smtp:UserName" "<your-gmail-address>" --project src/Ats.Worker
dotnet user-secrets set "Email:Smtp:Password" "<APP_PASSWORD>" --project src/Ats.Worker
dotnet user-secrets set "Email:FromAddress" "<your-gmail-address>" --project src/Ats.Worker
dotnet user-secrets set "Email:RedirectAllTo" "<your-gmail-address>" --project src/Ats.Worker
```

---

### Task 3: Email texts (EN/NL) and account tokens

**Files:**
- Create: `src/Ats.Application/Email/EmailTexts.resx`, `EmailTexts.nl.resx`, `src/Ats.Application/Email/EmailTemplates.cs`
- Create: `src/Ats.Application/Abstractions/IAccountTokens.cs`
- Create: `src/Ats.Web/Identity/DataProtectionAccountTokens.cs`; register in `src/Ats.Web/Program.cs`
- Create: `src/Ats.Application/Accounts/IAccountStore.cs`, `src/Ats.Infrastructure/Identity/AccountStore.cs`
- Test: `tests/Ats.Tests/Email/EmailTemplatesTests.cs`, `tests/Ats.Tests/Identity/AccountTokensTests.cs`

**Interfaces (produces):**
```csharp
public enum AccountTokenPurpose { PasswordReset, Invite, EmailChange }
public sealed record AccountToken(int UserId, int TenantId, Guid SecurityStamp, string? NewEmail = null);
public interface IAccountTokens
{
    string Create(AccountTokenPurpose purpose, AccountToken token);   // lifetime from purpose (Global Constraints)
    AccountToken? Read(AccountTokenPurpose purpose, string? token);    // null: tampered, expired, wrong purpose
}
public static class EmailTemplates   // pure; CultureInfo "en" until FEAT-5
{
    public static EmailDraft PasswordReset(string to, string displayName, string tenantName, string link);
    public static EmailDraft Invite(string to, string displayName, string tenantName, string inviterName, string link);
    public static EmailDraft EmailChangeVerify(string to, string displayName, string tenantName, string link);
    public static EmailDraft EmailChangeNotice(string oldAddress, string displayName, string tenantName, string newAddress);
    public static EmailDraft PasswordChanged(string to, string displayName, string tenantName);
    public static EmailDraft AccountDeactivated(string to, string displayName, string tenantName);
    public static EmailDraft NewApplication(string to, string tenantName, string candidateName, string jobTitle, string link);
    public static EmailDraft StageChanged(string to, string tenantName, string candidateName, string jobTitle, string stageName, string link);
}
// The sixth documented filter-bypass spot. Every read is scoped by an explicit TenantId and UserId from
// a validated token, or by the globally unique email (like ValidateCredentialsAsync).
public interface IAccountStore
{
    Task<AppUser?> FindByEmailAsync(string normalisedEmail, CancellationToken ct = default);
    Task<AppUser?> GetAsync(int tenantId, int userId, CancellationToken ct = default);
    Task<bool> TenantActiveAsync(int tenantId, CancellationToken ct = default);
    Task<string> TenantNameAsync(int tenantId, CancellationToken ct = default);
    Task<bool> EmailTakenAsync(string normalisedEmail, CancellationToken ct = default);
    void Audit(int tenantId, int userId, string userName, string action, string summary); // explicit TenantId
    Task SaveChangesAsync(CancellationToken ct = default);
}
```
Token format: JSON of `AccountToken`, protected by
`provider.CreateProtector("Ats.AccountTokens", purpose.ToString()).ToTimeLimitedDataProtector()`,
Base64Url. Inject `TimeProvider` so expiry is testable.

- [x] **Step 1: Failing tests.** Templates: every template HTML-encodes user values (`displayName =
  "<b>x</b>"` appears as `&lt;b&gt;x&lt;/b&gt;` in `HtmlBody`, raw in `TextBody`); the link appears in
  both bodies. A resource test
  asserts `EmailTexts.nl.resx` has exactly the keys of `EmailTexts.resx`. Tokens (with
  `EphemeralDataProtectionProvider` and a fake `TimeProvider`): round-trip; wrong purpose -> null;
  one-character change -> null; reset token after 61 minutes -> null; invite after 6 days -> valid.
- [x] **Step 2:** Run, fail; implement; pass.
- [x] **Step 3:** Add `IAccountStore` to `.claude/rules/multi-tenancy.md` as the sixth bypass spot (what
  it may read, that `Audit` stamps `TenantId` explicitly, that callers pass only token-validated ids).
- [x] **Step 4:** build, test, format.

Model: opus (tenancy bypass, authentication). Playwright: none.

---

### Task 4: Forgot password and reset (M1, part of M5)

**Files:**
- Create: `src/Ats.Application/Accounts/AccountRecoveryService.cs`
- Modify: `src/Ats.Web/Controllers/AccountController.cs`; views `ForgotPassword.cshtml`,
  `ResetPassword.cshtml` (Account), link "Forgot password?" on `Login.cshtml`
- Modify: `src/Ats.Web/Program.cs` (rate limiter policy `"account-email"`)
- Modify: `src/Ats.Application/Users/UserService.cs` (`ChangeOwnPasswordAsync` stages `PasswordChanged`)
- Modify: `playwright.config.ts` (`globalSetup`/`globalTeardown`), create `tests/e2e/mail-worker.setup.ts`,
  `tests/e2e/mailbox.ts`, `tests/e2e/password-reset.spec.ts`
- Test: `tests/Ats.Tests/Accounts/AccountRecoveryServiceTests.cs`

**Interfaces:**
- Consumes: `IAccountStore`, `IAccountTokens`, `IEmailOutbox`, `EmailTemplates`, `IIdentityService.HashPassword`,
  `IntegrationOptions.CareerSiteBaseUrl`.
- Produces:
```csharp
public interface IAccountRecoveryService
{
    Task RequestResetAsync(string email, CancellationToken ct = default);           // always completes silently
    Task<bool> IsResetTokenUsableAsync(string? token, CancellationToken ct = default);
    Task<OperationResult> ResetAsync(string? token, string newPassword, CancellationToken ct = default);
    Task SendResetLinkAsync(AppUser user, string tenantName, CancellationToken ct = default); // reused by Task 6
}
```
Rules: a reset is sent only for an existing, active, password-holding user in an active tenant. Link:
`{CareerSiteBaseUrl}/Account/ResetPassword?token=...`. `ResetAsync` checks token, user active, tenant
active, `SecurityStamp` equal; sets hash, `MustChangePassword = false`, new stamp, audits
`UserPasswordReset` ("Password reset by email link"), stages `PasswordChanged`. Password rules =
`UserService` rules (required, max 128). Routes: GET/POST `ForgotPassword`, GET/POST `ResetPassword`
(all `[AllowAnonymous]` via the controller). POST `ForgotPassword` always shows the same "If an account
exists, we have sent a link" view. Rate limiter: fixed window, 5 requests per 15 minutes per client IP,
on POST `ForgotPassword` (and Task 5/7 POSTs); rejection shows the same view with status 429. As built (whole-change
review fixes below): two policies, and a rejection is a plain-text 429, not the view.

- [x] **Step 1: Failing unit tests:** unknown email stages nothing; inactive user, invite-pending user
  (`PasswordHash == ""`), suspended tenant stage nothing; known user stages one draft whose link starts
  with the configured base URL (Review Focus 1); `ResetAsync` with a token whose stamp is stale fails;
  a successful reset rotates the stamp, so the same token fails the second time (Review Focus 3); reset
  stages `PasswordChanged`; `ChangeOwnPasswordAsync` stages `PasswordChanged`.
- [x] **Step 2:** Run, fail; implement service, controller, views (UI skill, design tokens,
  `data-page-title`); pass.
- [x] **Step 3: e2e mail plumbing.** `mail-worker.setup.ts` (globalSetup) empties `artifacts/mail`, then
  starts `dotnet run --project src/Ats.Worker` with `DOTNET_ENVIRONMENT=Development`,
  `Email__PickupDirectory=<abs>/artifacts/mail`, `Email__PollSeconds=1`, `Email__Smtp__Host=` (empty) and
  stores the PID; globalTeardown kills it. `mailbox.ts`: `waitForMail(to: string, subjectContains: string, timeoutMs = 20000)`
  polls the directory, parses the `.eml` (headers + text part), returns `{ subject, text, link }` where
  `link` is the first `https://` URL in the text part.
- [x] **Step 4: `password-reset.spec.ts`:** request reset for the test user -> mail arrives -> open link
  (GET twice; still works) -> set new password -> sign in with it -> restore the original password the
  same way. Forged `Host` header on POST `ForgotPassword` (via `page.request`) still yields a link on the
  configured base URL. Unknown email shows the same message and produces no mail within 5 seconds.
- [x] **Step 5:** build, test, format; Playwright: `password-reset.spec.ts`, `security.spec.ts`, `smoke.spec.ts`.

Model: opus (authentication). Shared files: `Program.cs`, `playwright.config.ts` -> full suite once.
Before e2e: stop any Worker you run yourself (it would send the test mail through Gmail).

---

### Task 5: Invite by email (M2)

**Files:**
- Modify: `src/Ats.Application/Users/UserService.cs` (`CreateAsync`, new `ResendInviteAsync`,
  `CountActiveOwnersAsync` semantics), `IUserRepository.cs`/`UserRepository.cs`, `UserListQuery`
  (`UserListItem` gains `bool InvitePending`)
- Modify: `src/Ats.Infrastructure/Identity/IdentityService.cs` (explicit guard: empty hash never signs in)
- Create: `src/Ats.Application/Accounts/InvitationService.cs`
- Modify: `UsersController` (`Create` without password, POST `ResendInvite`), `AccountController`
  (GET/POST `AcceptInvite`), views `Users/Create`, `Users/Index` ("Invited" badge), `Users/Edit`
  ("Resend invite" for pending users), `Account/AcceptInvite`; remove `TemporaryPassword` from `UserCreateViewModel`
- Modify: `tests/e2e/users.ts` and every spec that creates users through it; `tests/e2e/users.spec.ts`
- Test: `tests/Ats.Tests/Users/UserServiceTests.cs`, `tests/Ats.Tests/Accounts/InvitationServiceTests.cs`

**Interfaces:**
```csharp
public sealed record CreateUserInput(string DisplayName, string Email, string Role);  // TemporaryPassword removed
// As built: IUserService.CreateAsync creates the user invite-pending (PasswordHash = "", MustChangePassword = false)
// and sends nothing. UsersController.Create then calls IInvitationService.SendInviteAsync, a second save; if it
// fails the user stays pending and the Owner uses Resend invite.
public interface IInvitationService
{
    Task<OperationResult> SendInviteAsync(int userId, CancellationToken ct = default);
    Task<OperationResult> ResendAsync(int userId, CancellationToken ct = default); // rotates stamp: old link dies
    Task<AccountToken?> ReadUsableAsync(string? token, CancellationToken ct = default);
    Task<SignInResult> AcceptAsync(string? token, string password, CancellationToken ct = default); // sets hash, new stamp, audit "UserInviteAccepted"
}
```
`AppUser.IsInvitePending => PasswordHash.Length == 0` (not mapped). An invite-pending Owner does not
count as an active Owner for the last-Owner rule. Inviter name comes from `ICurrentUser`. After accept
the user is signed in through `AtsSignIn` and lands on the dashboard. Audit `UserCreated` text becomes
"Invited '<email>' as <role>"; `UserInviteResent` for resend. Deactivating a pending user kills the
link (stamp rotation, as today). Success message: "Invitation sent to <email>."

- [x] **Step 1: Failing tests:** create stages exactly one `Invite` to the new address and sets no
  password; resend on a non-pending user fails ("This user has already accepted."); accept with a
  stale stamp, deactivated user or a used token fails; accept twice fails; a pending Owner is not
  counted by the last-Owner guard; `ValidateCredentialsAsync` fails for an empty hash with any password.
- [x] **Step 2:** Run, fail; implement; pass.
- [x] **Step 3:** e2e: rewrite `users.ts` so `createUser(...)` invites, waits for the mail, accepts with a
  generated password and returns it; update `users.spec.ts` (invite -> accept -> sign in; resend
  invalidates the first link; "Invited" badge shown until accepted).
- [x] **Step 4:** build, test, format; Playwright: `users.spec.ts`, `rbac.spec.ts`, `hiring-manager.spec.ts`,
  `security.spec.ts`, `smoke.spec.ts`; full suite once (`users.ts` is shared by several specs).

Model: opus (authentication, sessions).

---

### Task 6: Owner sends a reset link; deactivation notice (M3, rest of M5)

**Files:**
- Modify: `UserService.ResetPasswordAsync` -> `SendResetLinkAsync(int userId, int actingUserId, ct)`;
  `UserService.SetActiveAsync` stages `AccountDeactivated` when deactivating
- Modify: `UsersController.ResetPassword` (no password field; POST only, `hx-confirm`); `Users/Edit.cshtml`;
  delete `UserResetPasswordViewModel`
- Modify: `tests/e2e/users.spec.ts`
- Test: `tests/Ats.Tests/Users/UserServiceTests.cs`

Rules: the Owner's action sends the same email as Forgot password (`IAccountRecoveryService.SendResetLinkAsync`);
it does **not** change the password or sign the user out (deactivate for a compromised account). Fails
for self ("Use Change password for your own account."), inactive or invite-pending users. Audit
`UserPasswordResetSent`. Message: "Reset link sent to <email>."

- [x] **Step 1: Failing tests:** sending a link stages one `PasswordReset` and leaves `PasswordHash` and
  `SecurityStamp` unchanged; self, inactive, pending fail; deactivation stages `AccountDeactivated`,
  reactivation stages nothing.
- [x] **Step 2:** Run, fail; implement; pass.
- [x] **Step 3:** e2e: Owner clicks "Send reset link", confirms, the mail arrives, the link works.
- [x] **Step 4:** build, test, format; Playwright: `users.spec.ts`, `row-menus.spec.ts`, `smoke.spec.ts`.

Model: opus (authentication).

---

### Task 7: Verified email change (M4)

**Files:**
- Modify: `UserService.UpdateAsync` (email part), `UserUpdateResult` (`PendingEmail`), `UsersController.Edit`
- Create: `src/Ats.Application/Accounts/EmailChangeService.cs`; `AccountController` GET/POST `ConfirmEmail`; view `Account/ConfirmEmail`
- Test: `tests/Ats.Tests/Users/UserServiceTests.cs`, `tests/Ats.Tests/Accounts/EmailChangeServiceTests.cs`; `tests/e2e/users.spec.ts`

Rules: for an accepted user, an email change no longer saves the email. It checks uniqueness, then
stages `EmailChangeVerify` to the new address (token carries `NewEmail`) and `EmailChangeNotice` to the
old one; name and role changes in the same save apply as today. `ChangedFields` does not contain
`email`; `PendingEmail` holds it; message "Verification sent to <new>. The address changes when they
confirm." For an invite-pending user the email changes at once and a new invite goes to the new
address (stamp rotates, old link dies). Confirm (POST) re-checks stamp, active user, active tenant and
global uniqueness (`IAccountStore.EmailTakenAsync`, then the unique index as the final guard), sets the
email, rotates the stamp, audits `UserEmailChanged`. Audit at request: `UserEmailChangeRequested`.
As built: the request rotates the stamp (each request kills earlier links); see Whole-change review fixes.

- [x] **Step 1: Failing tests:** change on an accepted user stages two drafts and leaves `Email` unchanged;
  confirm sets it and rotates the stamp; second confirm fails; confirm after another user took the
  address fails with "That email address is already registered."; pending user email change applies at
  once with one `Invite`.
- [x] **Step 2:** Run, fail; implement; pass.
- [x] **Step 3:** e2e in `users.spec.ts`: change a user's email -> list still shows the old one -> confirm
  link from the new address's mail -> list shows the new one; the old address got the notice.
- [x] **Step 4:** build, test, format; Playwright: `users.spec.ts`, `smoke.spec.ts`.

Model: opus (authentication, tenancy bypass).

---

### Task 8: Hiring-team notifications (M6, M7)

**Files:**
- Create: `src/Ats.Application/Notifications/IHiringNotifier.cs`, `HiringNotifier.cs`,
  `INotificationRecipientsQuery.cs`; `src/Ats.Infrastructure/Notifications/NotificationRecipientsQuery.cs`
- Modify: `CareerService.ApplyAsync` (new application from the career site only; a recruiter adding a
  candidate in the back office notifies no one), `ApplicationService.MoveStageAsync`, DI
- Test: `tests/Ats.Tests/Notifications/HiringNotifierTests.cs`, extend `ApplicationServiceTests`, `CareerServiceTests`

**Interfaces:**
```csharp
public sealed record Recipient(int UserId, string Email);
public interface INotificationRecipientsQuery
{
    // Active, accepted users: role Recruiter, plus the job's assigned HiringManagers; when the tenant has
    // no active Recruiter, its active Owners instead of Recruiters.
    Task<IReadOnlyList<Recipient>> NewApplicationAsync(int jobId, CancellationToken ct = default);
    // Active, accepted HiringManagers assigned to the job.
    Task<IReadOnlyList<Recipient>> StageChangeAsync(int jobId, CancellationToken ct = default);
}
public interface IHiringNotifier   // stages only; the caller's save commits
{
    Task StageNewApplicationAsync(int jobId, string jobTitle, string candidateName, CancellationToken ct = default);
    Task StageStageChangedAsync(int jobId, string jobTitle, string candidateName, string stageName, int? movedByUserId, CancellationToken ct = default);
}
```
Link: `{CareerSiteBaseUrl}/Board?jobId={jobId}`. The mover never gets their own move. A re-apply to the
same job sends nothing (no new application). Recipients are deduplicated by user id. Both run inside
the existing unit of work, so a failed save sends nothing. The query uses the normal tenant filter (no bypass).

- [x] **Step 1: Failing tests:** new application from the career site stages one draft per recipient;
  re-apply stages none; a move stages one per assigned active HiringManager and none for the mover;
  no assigned HiringManager -> none; a move that is a no-op stages none; a concurrency-failed move
  stages none (draft is discarded with the failed save: assert through the fake repository's save result).
- [x] **Step 2:** Run, fail; implement; pass.
- [x] **Step 3:** build, test, format; Playwright: `career-site.spec.ts`, `journeys.spec.ts` (behaviour
  unchanged on screen; checks nothing broke). `*ListQuery`/Application changes here are not shared files.

Model: opus (tenant data path).

---

### Task 9: Documentation (mandatory phase close)

**Files:** `.claude/skills/notifications/SKILL.md` (new), `CLAUDE.md` (skill index row "Notifications",
convention line on email), `.claude/skills/authorization/SKILL.md` (invite, reset link, email change),
`docs/specs/2026-09-30-rbac-design.md` (D1 supersedes the temporary-password decision),
`docs/review/phase-07-feature-completeness-and-workflows.md` (tick FEAT-1, FEAT-2, FEAT-4),
`README` or `docs` note with the user-secrets commands and the e2e "stop your own Worker" rule.

- [x] **Step 1:** Write the skill: queue table, staging rule, Worker loop, classification, body purge,
  dev redirect, pickup directory and e2e mailbox, tokens and lifetimes, recipients rules, follow-ups
  M8 to M10 and "production needs a transactional provider with SPF/DKIM/DMARC on our domain".
  Also updated: `CLAUDE.md`, the multitenancy, architecture, integration and UI skills, the multi-tenancy
  rule, the RBAC and product specs, and the phase 7 review. The user-secrets commands and the "stop your own
  Worker" rule are in the notifications skill (no separate README note).
- [x] **Step 2:** `dotnet build`, `dotnet format --verify-no-changes` (no code expected).

Model: sonnet. Playwright: none.

## Review follow-ups (28 October 2026, done)
- Subject CR/LF sanitising: `EmailTemplates` strips control characters from user values in subjects, and
  `EmailDraft.Validate` rejects a subject with a line break.
- Shared `Ats.Application.Common.EmailAddress` helper (normalise, validate, error texts) used by users,
  account links and the Users screen.
- Test for an email change on a pending (invite) user.
- Hint on the Users Edit page about the verified email change.
- AcceptInvite shows the email as a read-only field.

## Whole-change review fixes (28 October 2026)
1. **A pending email change can be withdrawn.** `EmailChangeService.RequestAsync` rotates the user's
   `SecurityStamp` and mints the token with the new stamp, in the same save as the two staged emails. Each new
   request kills every earlier verify link (a mistyped address can no longer confirm and take the account over
   through Forgot password) and signs the user out. Users > Edit says "They are signed out." and the hint reads
   "Changing the role or email signs the user out."
2. **No 500s from email validation inside business saves.**
   - `EmailTemplates` cuts a subject longer than 300 characters to 297 plus "...".
   - `EmailOutbox.Stage` skips a draft whose recipient fails `EmailDraft.HasValidRecipient` (Warning log with the
     tenant id, no address); a blank, over-long or CR/LF subject still throws.
   - `TenantOnboardingService` validates and normalises the owner email with `EmailAddress`.
3. **Rate limits split.** `account-email` (`RateLimits:AccountEmailPermitLimit`, default 5 per 15 minutes per IP) is
   on POST `ForgotPassword` only; `account-token` (`RateLimits:AccountTokenPermitLimit`, default 30) is on POST
   `ResetPassword`, `AcceptInvite` and `ConfirmEmail`, so one office behind a NAT IP can onboard a team. A rejection
   stays a plain-text 429 (the limit is per IP, so it reveals nothing about an address). The Playwright web server
   raises both.

## Follow-ups (not in this plan)
- M8 applicant confirmation, M9 "assigned as hiring manager", M10 sign-up email verification.
- Per-user notification preferences (opt-out) if recipients find M6/M7 noisy.
- Production email provider (Azure Communication Services Email or SendGrid) and domain authentication.
- Per-email throttle on Forgot password (today only per IP).
- A lighter job-title read in `ApplicationService.MoveStageAsync`.
