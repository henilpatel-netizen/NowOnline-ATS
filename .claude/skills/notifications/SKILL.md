---
name: notifications
description: Ats transactional email - the EmailMessages queue, Worker delivery and retry, account links (reset, invite, email change), hiring-team notifications, dev SMTP setup and the e2e mailbox. Read before sending or changing any email.
---

# Ats Email and Notifications

Decisions of 28 October 2026 (plan: `docs/plans/2026-10-28-email-notifications.md`): D1 invites and reset
links replace Owner-typed temporary passwords; D2 stage-change mail goes to the hiring team only, never to
candidates; D3 scope M0 to M7; D4 every text has EN and NL.

## Queue and staging
- **`EmailMessages`** is a global table (not an `ITenantEntity`, like `Tenants`): `To`, `Subject`, `HtmlBody`,
  `TextBody`, `Status` (`OutboxStatus`), `Attempts`, `NextAttemptAt`, `LastError`, `TenantId` (informational,
  never a filter). Index `(Status, NextAttemptAt)`. Written only through `IEmailOutbox`, read only by the
  Worker; never expose it to a back-office query.
- **Web and Application code never talk to SMTP.** Render an `EmailDraft` with `EmailTemplates`, then:
  - `IEmailOutbox.Stage(draft, tenantId)` adds the row to the context; the caller's next `SaveChanges` commits it
    **in the same unit of work** as the change that caused it. A failed save sends nothing. Use this whenever
    the caller saves anyway.
  - `IEmailOutbox.SendAsync(draft, tenantId, ct)` is Stage plus save, for callers with nothing else to save
    (forgot password, send reset link).
- **`Stage` skips an unusable recipient.** A draft that fails `EmailDraft.HasValidRecipient` (blank, invalid, over
  256) is not staged; `EmailOutbox` logs a Warning with the tenant id and never the address. Stored addresses may
  predate the stricter rule, so a password change, deactivation, reset or notification for such a user still saves.
  Then `EmailDraft.Validate` throws for an empty subject, one over 300 or one containing CR or LF: those come from
  our templates, so they are programmer errors.

## Worker delivery (`Ats.Worker`)
`EmailDrainer` polls every `Email:PollSeconds` (10). Per pass:
1. `EmailClaimStore.ClaimDueAsync` claims up to `Email:BatchSize` (20) rows atomically: `UPDATE TOP` over a CTE
   `WITH (READPAST, UPDLOCK, ROWLOCK)`, Pending or lease-expired Processing rows with `NextAttemptAt <= now`,
   `OUTPUT inserted.Id, inserted.TenantId, inserted.NextAttemptAt AS Lease`. The `OutboxClaimStore` pattern
   without the ordering `NOT EXISTS` (emails are independent). Raw SQL, but the table has no filter, so no
   bypass.
2. The lease is `Email:ClaimLeaseSeconds` (default 120, validated minimum 120). `NextAttemptAt` doubles as
   the fencing token and the concurrency token: `EmailProcessor` only touches a row still `Processing` with
   exactly that lease (`EmailClaim.IsOwnedBy`); a lost claim is logged and the outcome not recorded.
3. `EmailRules.HasLeaseFor`: a send starts only with `WorstCaseSendSeconds` (90: connect, authenticate and
   send can each take one 30 s SMTP timeout) of lease left, so a send cannot outlive its claim and be sent
   again by another pass. Skipped claims return when the lease expires (the drainer warns).
4. Per message, failures are isolated: an exception is logged by type only and recorded through
   `RecordFailureAsync` (a Transient failure) in a fresh scope; shutdown cancellation propagates.

**Classification** (`EmailRules.Classify`, pure, unit-tested; change the rules there):
- Only a **5xx rejection of the recipient** (`SmtpErrorCode.RecipientNotAccepted`) is **Permanent**, and not
  when its enhanced status is `5.4.x` (Gmail answers its daily quota with `550 5.4.5`): `5.4.x` is always
  **Transient**.
- Every other SMTP error reply (4xx, and 5xx about the sender, message or session such as 530, 552, 554) is
  Transient, as are connection, TLS, protocol, authentication, IO and timeout failures.
- `InvalidRecipientException` (an address MailKit refuses to build) is Permanent; a row with no body at all is
  Permanent ("No body").

**Settling** (`EmailRules.Settle`): Sent -> `Delivered`; Permanent -> `Failed`; Transient -> `Attempts++`, back
to `Pending` with `NextAttemptAt = now + min(MaxBackoffSeconds, BaseBackoffSeconds * 2^attempts)`, and
`Failed` once `Attempts` reaches `Email:MaxAttempts` (8). **Bodies are cleared on Delivered and Failed**, so no
live link sits in the database after sending.

**What may be stored or logged.** `LastError` holds the SMTP status, the enhanced status code (extracted by
`EmailRules.EnhancedStatus`), the `SmtpErrorCode` and the exception type only: never the body, the password
or the server's reply text (RCPT replies echo the recipient). Logs carry `MessageId` and `TenantId`, never the
recipient address or the body. Never log `SmtpCommandException.Message`.

## Configuration (`Email` section, bound by `Ats.Worker`)
Numeric defaults live in `Ats.Worker/appsettings.json`; `appsettings.Development.json` adds
`Smtp:Host smtp.gmail.com`, `Port 587`, `Security StartTls` and `FromName "Ats (dev)"`. Nothing secret is in
either file. `EmailOptions.Validate` runs at start-up (`ValidateOnStart`): `RedirectAllTo` outside Development,
neither `PickupDirectory` nor `Smtp:Host`, a missing or invalid `FromAddress`, an unknown `Security`
(`None`, `StartTls`, `SslOnConnect`), a bad port, and out-of-range numbers are errors.
**The Worker refuses to start without a valid Email section.** That includes the outbox drainer, which lives
in the same process.

### Development setup (Gmail)
Gmail sends as the authenticated account, so **`FromAddress` must equal `Smtp:UserName`**. Quota is about 500
messages a day. Use an app password, never the account password. Secrets live in user-secrets on `Ats.Worker`
(it has a `UserSecretsId`); placeholders here, real values never in chat, files or logs:
```
dotnet user-secrets set "Email:Smtp:UserName" "<your-gmail-address>" --project src/Ats.Worker
dotnet user-secrets set "Email:Smtp:Password" "<APP_PASSWORD>" --project src/Ats.Worker
dotnet user-secrets set "Email:FromAddress" "<your-gmail-address>" --project src/Ats.Worker
dotnet user-secrets set "Email:RedirectAllTo" "<your-gmail-address>" --project src/Ats.Worker
```
- **`Email:RedirectAllTo`** is Development only: every message goes to that address with the subject
  prefixed `[to: <original>] ` (`EmailRules.Route`). Set it, or a test user's mail goes to a real mailbox.
- **`Email:PickupDirectory`**: when set, the transport writes `{dir}/{UtcNow:yyyyMMddHHmmssfff}-{Guid}.eml`
  instead of sending. Used by the e2e suite.

### Other environments
Set `Email__*` (host, port, security, user, password, `FromAddress`) as environment variables or secrets for
the Worker **before deploying**, or the Worker will not start. `Integration__CareerSiteBaseUrl` must be set
for Web as well (links). Production needs a transactional provider (see Follow-ups).

## e2e mail plumbing
- `playwright.config.ts` has `globalSetup: tests/e2e/mail-worker.setup.ts` and a teardown. Setup empties
  `artifacts/mail`, starts `dotnet run --project src/Ats.Worker` with `DOTNET_ENVIRONMENT=Development`,
  `Email__PickupDirectory=<abs>/artifacts/mail`, `Email__PollSeconds=1`, `Email__Smtp__Host=` and
  `Email__RedirectAllTo=` (both empty, overriding a developer's user-secrets), stores the PID and waits for
  "Application started". Teardown kills the process tree.
- `tests/e2e/mailbox.ts`: `waitForMail(to, subjectContains, timeoutMs = 20000)` returns an unread
  `{ subject, text, link }` (`link` is the first `https://` URL in the text part) and marks it read;
  `expectNoMail(to, subjectContains, waitMs = 5000)` waits, then fails if a matching unread mail exists. Mail
  written before the module loaded is never matched.
- **Stop any Worker you run yourself before Playwright.** It would also drain the queue and send the test
  mail through Gmail (or race the test Worker).
- Every e2e request comes from 127.0.0.1, so the account rate limits would trip. `webServer.env` sets
  `RateLimits__AccountEmailPermitLimit=100` and `RateLimits__AccountTokenPermitLimit=300`. **`reuseExistingServer`
  is true:** a Web server you started yourself keeps its own limits (5 and 30 per 15 minutes); stop it, or start it
  with the variables set.
- `users.ts` `createUser(...)` invites, waits for the mail, accepts with a generated password and returns it;
  use it for any spec that needs a second user.

## Account links (reset, invite, email change)
- **Tokens** are stateless: `IAccountTokens` (`Ats.Application/Abstractions`), implemented by
  `Ats.Web/Identity/DataProtectionAccountTokens` over ASP.NET Core Data Protection, **purpose-bound**
  (`CreateProtector("Ats.AccountTokens", purpose)` for `PasswordReset`, `Invite`, `EmailChange`). The payload
  is JSON `AccountToken(UserId, TenantId, SecurityStamp, NewEmail?)` plus `ExpiresAt`, checked against
  `TimeProvider` (the time-limited protector reads the system clock, which cannot be tested). `Read` returns
  null for tampered, expired or wrong-purpose tokens. Lifetimes: **password reset 1 h, invite 7 d, email
  change 24 h**.
- Tokens **carry the user's `SecurityStamp`**. Every service compares it with the loaded user's stamp, so any
  security change, and using the link itself (which rotates the stamp), kills the link. No token table.
  Data Protection keys must be shared across Web instances (`DataProtection:KeyPath`).
- **A token is consumed only by a POST.** GET renders a form (or the "expired" view) and changes nothing,
  because mail scanners prefetch links. The GET actions send `Referrer-Policy: no-referrer` (the token is in
  the URL).
- **Rate limits** (`Ats.Web/Program.cs`, fixed 15-minute window per client IP):
  - `account-email` (`RateLimits:AccountEmailPermitLimit`, default 5) on POST `ForgotPassword` only: it mails any
    address a stranger types in.
  - `account-token` (`RateLimits:AccountTokenPermitLimit`, default 30) on POST `ResetPassword`, `AcceptInvite` and
    `ConfirmEmail`: they need a valid emailed link, and an office behind one NAT IP must be able to onboard a team.
  - A rejection is a plain-text 429 ("Too many requests. Please try again later."), not the Forgot password view.
    The limit is per IP, so it reveals nothing about any address.
- **Links come from `Integration:CareerSiteBaseUrl` only**, never from the request's `Host` header (host
  header poisoning; tested).
- **The three services** (`Ats.Application/Accounts`):
  - `AccountRecoveryService`: `RequestResetAsync` (always silent: unknown, inactive, invite-pending or
    suspended-tenant emails get no mail and the same page), `IsResetTokenUsableAsync`, `ResetAsync` (sets
    hash, clears `MustChangePassword`, rotates the stamp, audits `UserPasswordReset`, stages
    `PasswordChanged`), `SendResetLinkAsync` (Owner action; changes neither password nor stamp; fails for self,
    inactive and pending users).
  - `InvitationService`: `SendInviteAsync`, `ResendAsync` (rotates the stamp, old link dies), `ReadUsableAsync`,
    `AcceptAsync` (sets the first password, rotates the stamp, audits `UserInviteAccepted`, returns what
    `AtsSignIn` needs). A user with an empty `PasswordHash` (`AppUser.IsInvitePending`) can never sign in.
  - `EmailChangeService`: `RequestAsync` **rotates the stamp** and mints the token with the new one, in the same
    save as the verify link to the new address and the notice to the old one. So each request kills every earlier
    verify link (a mistyped address cannot confirm later and take the account over through Forgot password) and
    signs the user out; Users > Edit says "They are signed out." `RequestAsync` sets a new stamp, mints the link
    from it and stages both emails in one save. `ConfirmAsync` re-checks stamp, active user, active tenant and uniqueness (`EmailTakenAsync`, then the
    unique index through `IAccountStore.TrySaveAsync`), sets the email, rotates the stamp, audits
    `UserEmailChanged`.
  - **They are registered in `Ats.Web/Program.cs`, not `AddAtsInfrastructure`:** they need `IAccountTokens`,
    which only Web provides, and the Worker validates its registrations at start-up (in Infrastructure they would
    fail the Worker's container).
  - Signed-out paths read through `IAccountStore`, the sixth documented filter-bypass spot (multitenancy
    skill). Back-office paths (`SendInviteAsync`, `SendResetLinkAsync`, `RequestAsync`) read through
    `IUserRepository` under the normal tenant filter.
- Normalise addresses with `Ats.Application.Common.EmailAddress` (`Normalise`, `IsValid`, the shared error
  texts); users, account links and the Users screen share it.

## Templates and texts
`EmailTemplates` (pure) renders eight emails: `PasswordReset`, `Invite`, `EmailChangeVerify`,
`EmailChangeNotice`, `PasswordChanged`, `AccountDeactivated`, `NewApplication`, `StageChanged`. Texts live in
`EmailTexts.resx` (EN) and `EmailTexts.nl.resx` (NL); a test asserts both have the same keys, so add every key
to both. **Rendered in `en` until FEAT-5** adds culture selection. All user values are HTML-encoded in the HTML
body and raw in the text body; the link is in both. **Subjects strip control characters** (CR, LF) from user
values, so a job title cannot inject headers; `EmailDraft.Validate` is the second guard. **A subject over 300
characters** (`EmailTemplates.MaxSubjectLength`, the column limit; e.g. a 200-character candidate name and a long
stage name) is cut to 297 and ends with "...".

## Hiring-team notifications
`IHiringNotifier` (`HiringNotifier`) only stages; the caller's save commits, so a failed or concurrency-failed
save sends nothing. Wired into `CareerService.ApplyAsync` and `ApplicationService.MoveStageAsync`.
- **New application** (career-site apply only; a recruiter adding a candidate in the back office notifies no
  one; re-applying to the same job sends nothing): active, accepted **Recruiters**, plus the job's assigned
  **HiringManagers**; **Owners instead of Recruiters** when the tenant has no active Recruiter.
- **Stage change**: the job's assigned active, accepted HiringManagers; **never the user who moved it, never
  candidates**; a no-op move sends nothing.
- One message per recipient (no shared To/CC), deduplicated by user id. Link: `{CareerSiteBaseUrl}/Board?jobId={id}`.
  Mail carries candidate name, job title and the link only: **no candidate email, phone or CV**.
- The rules are `NotificationRecipientFilter` (plain LINQ in Application, unit-tested; `Reachable` = active
  and non-empty password hash) applied by `NotificationRecipientsQuery` to the tenant-filtered `Users`; the
  job team comes through `db.Jobs`, so a soft-deleted job has no team. No bypass.
- Other staged mail: `UserService` stages `PasswordChanged` (own password change) and `AccountDeactivated`
  (deactivation only; reactivation sends nothing).

## Known limitations
- **At-least-once delivery.** A message is sent twice if the DB save fails after a successful send, or if the
  SMTP server stalls past the lease (a lost claim after send is logged as a warning).
- **Start-up needs Email config in every environment** (see Other environments).
- **The rate limits are keyed on `RemoteIpAddress`.** Behind a proxy or load balancer, configure forwarded
  headers or all users share one bucket.
- **Timing difference on Forgot password:** a known address does the extra insert, so response time can reveal
  it. The response body is identical; accepted for now.
- `EnableRetryOnFailure` re-running a unit of work can stage the same email twice (the pre-existing pattern of
  every staged write).
- `AppUser` has no concurrency token, so two simultaneous POSTs with the same link can both succeed.

## Follow-ups (not built)
- M8 applicant confirmation, M9 "assigned as hiring manager", M10 sign-up email verification.
- Per-user notification preferences (opt-out) if M6/M7 prove noisy.
- A production provider (Azure Communication Services Email or SendGrid) with SPF, DKIM and DMARC on our
  sending domain. Gmail is a development transport only.
- Per-email throttle on Forgot password (today only per IP).
- A lighter job-title read in `ApplicationService.MoveStageAsync` (it loads the job for the title).
