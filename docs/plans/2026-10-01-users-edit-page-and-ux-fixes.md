# Users Edit Page and UX Fixes Implementation Plan

> **For agentic workers:** run through `/ats-ship docs/plans/2026-10-01-users-edit-page-and-ux-fixes.md`.
> Project overrides apply: implementers never commit or stage; they leave a working-tree diff.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Users row menu with one Edit page that holds every user change, drop the
12-character password minimum for now, and fix the UX issues found in a browser pass on 1 October 2026.

**Architecture:** `UserService` gains one `UpdateAsync` (name, email, role) that runs inside the existing
serialisable `InTransactionAsync`; the Users list gets search and a status filter through the
repository. The Edit page shows details plus an actions section (reset password, deactivate or
reactivate) according to who is editing whom. No migration.

**Tech Stack:** .NET 10 MVC, EF Core, xUnit, Playwright.

**Decisions (developer, 1 October 2026):**
- Remove the "at least 12 characters" password rule for now (to be reinstated later). Keep "required"
  and the 128-character maximum (hashing very long inputs is a denial-of-service risk).
- An Owner can edit another user's **name, email and role**.
- An Owner's own row also gets **Edit**, but with **name only** (role, email, reset and deactivate are
  hidden for their own account; own password stays on Change password).

## Browser pass findings (1 October 2026, Owner and a throwaway Viewer)
| # | Finding | Task |
|---|---|---|
| F1 | Users row menu splits actions over Change role / Reset password pages; no way to edit name or email | 2, 3 |
| F2 | Users list has 45 rows, mostly deactivated e2e users, and no search, filter or paging | 4 |
| F3 | While held on Change password (temporary password), sidebar links, search and notifications are shown but every one bounces back | 5 |
| F4 | Jobs list for a Viewer shows a row menu whose only item, "Open board", duplicates the row title link | 5 |
| F5 | Audit log free-text search does not match the action or entity type ("User" finds nothing although User entries exist); `%` and `_` in the query act as wildcards | 5 |
| F6 | 12-character minimum blocks shorter passwords (developer request) | 1 |

Checked and fine: Viewer is held on the change page and released after changing; Viewer sees jobs,
job details and board read-only with no Add candidate and no stage select; deactivate confirm modal and
flash; 403 pages; icon spans are `aria-hidden`.

## Out of scope
- Paging on Users (search and filter are enough for tenant-sized lists; add paging if a tenant passes a
  few hundred users).
- Email verification on email change (no email integration yet).
- Re-adding a password minimum (later, per the decision above).

---

### Task 1: Remove the 12-character password minimum

**Model:** opus (authentication rules).

**Files:** `src/Ats.Application/Users/UserService.cs`, `src/Ats.Web/Models/ChangePasswordViewModel.cs`,
`src/Ats.Web/Models/UserViewModels.cs`, `src/Ats.Web/Views/Profile/ChangePassword.cshtml`,
`src/Ats.Web/Views/Users/Create.cshtml`, `src/Ats.Web/Views/Users/ResetPassword.cshtml` (deleted in Task 3,
edit it only if Task 3 has not run), `tests/Ats.Tests/Users/UserServiceTests.cs`,
`.claude/skills/authorization/SKILL.md`, `docs/specs/2026-09-30-rbac-design.md`.

- [x] **Step 1: Tests first.** In `UserServiceTests`: replace the "short password rejected" cases
  (`Create_rejects_short_passwords`, the admin-reset short-password test, `New_password_must_be_long_enough`)
  with: a blank or whitespace-only password is rejected; a 1-character password is accepted; a
  129-character password is still rejected (create, reset and own change). Run: they fail.
- [x] **Step 2: Service.** Delete `MinPasswordLength`. `PasswordError` becomes: null, empty or whitespace
  only -> `"Enter a password."`; longer than `MaxPasswordLength` -> `"Password must be 128 characters or fewer."`
  (build the number from the constant). Keep the "new password must differ from current" rule.
- [x] **Step 3: View models.** Every `[StringLength(UserService.MaxPasswordLength, MinimumLength = ...)]`
  becomes `[StringLength(UserService.MaxPasswordLength)]`. `[Required]` stays.
- [x] **Step 4: Views.** Remove "At least N characters." from the help texts. Create: "Share it
  securely; the user must change it when they first sign in." Change password: drop the help line and its
  `aria-describedby`.
- [x] **Step 5: Docs.** Authorization skill and RBAC spec: passwords are required, max 128; the minimum
  is removed by decision on 1 October 2026, to be reinstated later.
- [x] **Step 6: Verify.** build 0 warnings, `dotnet test`, `dotnet format --verify-no-changes`. Mutation:
  re-add a minimum of 2, confirm the 1-character test fails, restore.

---

### Task 2: `UserService.UpdateAsync` (name, email, role)

**Model:** opus (security rules, tenant-scoped data, transaction).

**Files:** `src/Ats.Application/Users/IUserRepository.cs`, `UserService.cs`,
`src/Ats.Infrastructure/Persistence/Repositories/UserRepository.cs`, `tests/Ats.Tests/Fakes/FakeUserRepository.cs`,
`tests/Ats.Tests/Users/UserServiceTests.cs`.

Rules:
| Rule | Detail |
|---|---|
| One entry point | `Task<UserUpdateResult> UpdateAsync(UpdateUserInput input, int actingUserId, CancellationToken ct)` (Result, ChangedFields, SignedOut) with `UpdateUserInput(int UserId, string DisplayName, string Email, string Role)` |
| Self edit | When `UserId == actingUserId` only the name may change. A changed email or role on self returns `"You can only change your own name here."` and saves nothing |
| Name | Required, trimmed, max 200 (same as create) |
| Email | Trimmed, lower-cased, valid (same check as create). Uniqueness is checked only when the email changed: `IOnboardingStore.EmailExistsAsync` true -> `"That email address is already registered."`. A unique-index race on save is translated the same way |
| Role | Must be in `AtsRole.Assignable`. Demoting the last active Owner -> the existing last-Owner message |
| Stamp | Rotate `SecurityStamp` when email or role changed (the user is signed out). A name-only change does not rotate |
| No-op | Nothing changed -> `Ok`, no save, no rotation |
| Transaction | The whole load, checks and save run inside `_repo.InTransactionAsync` (serialisable), like `ChangeRoleAsync` today |
| Unknown user | `"User not found."` (tenant-filtered load) |

- [x] **Step 1: Repository.** Add `Task<bool> TrySaveAsync(CancellationToken ct)` that saves and returns
  false only on the `IX_Users_Email` unique violation (reuse the detection in `TryAddAsync`; extract a
  private helper), leaving other exceptions alone. Fake: a `RejectNextSaveAsDuplicate` flag.
- [x] **Step 2: Tests first** for every rule row above, plus: role change still rotates; email change
  rotates; name-only change does not rotate; email unchanged but different case/whitespace counts as
  unchanged; runs in one transaction (`TransactionCount == 1`). Run: they fail.
- [x] **Step 3: Implement** `UpdateAsync`. Remove `ChangeRoleAsync` and its tests once `UpdateAsync`
  covers them (no other callers after Task 3; check with LSP).
- [x] **Step 4: Verify** build, tests, format; mutation-check the self-edit guard and the email-change
  rotation.

---

### Task 3: Edit page with every user action; row shows Edit only

**Model:** sonnet (UI on top of Task 2's rules).

**Files:** `src/Ats.Web/Controllers/UsersController.cs`, `src/Ats.Web/Models/UserViewModels.cs`,
`src/Ats.Web/Views/Users/Index.cshtml`, `Edit.cshtml`, delete `Views/Users/ResetPassword.cshtml`,
`tests/Ats.Tests/Authorization/ControllerAuthorizationTests.cs`, `tests/e2e/users.spec.ts`,
`.claude/skills/ui/SKILL.md` (only if it documents the Users screen).

Behaviour:
- **List row:** the `...` dropdown is replaced by one Edit icon link (`ms` icon `edit`,
  `aria-label="Edit <name>"`, same pattern as `Views/Organisation/Index.cshtml`) on **every** row,
  including the Owner's own row (the "You" chip moves next to the name).
- **Edit page** (`GET /Users/Edit/{id}`, title "Edit user"):
  - **Details card:** Name, Email, Role (select from `RoleOptions.For`). For the Owner's own record,
    Email and Role are shown read-only and only Name is an input, with the note "You can change your role
    and email only through another Owner." Save button "Save changes". Help text under the form:
    "Changing the email or role signs the user out."
  - **Actions card** (hidden entirely for the Owner's own record):
    - Active user: "Reset password" (temporary password field + button, `hx-confirm` danger) and
      "Deactivate" (button, `hx-confirm` danger).
    - Deactivated user: only "Reactivate" (button, `hx-confirm`). Details stay editable.
  - Status pill next to the title (Active / Deactivated).
- **Controller:**
  - `GET Edit(id)`: 404 when not found; no longer redirects for self.
  - `POST Edit(UserEditViewModel)`: calls `UpdateAsync`; on success flash "User updated." (plus
    " They are signed out." when email or role changed; the service can return that via the
    `OperationResult` message pattern or the controller can compare with the stored record). Audit
    `UserUpdated` with a summary built from the stored record before and after, listing only what
    changed, for example `Updated 'old@acme.test': email to 'new@acme.test', role to Recruiter, name`.
    Never a password. If the
    acting Owner changed their own name, re-issue their cookie with the new name (same approach as
    `ProfileController.ChangePassword`, using `IIdentityService.GetSessionAsync`).
  - `POST ResetPassword(id, TemporaryPassword)` and `POST Deactivate(id)` / `POST Reactivate(id)`:
    redirect back to `Edit/{id}` with the flash (errors as `TempData["Error"]`; a reset validation
    error redisplays Edit with the field error). Audit entries unchanged.
  - Remove `GET ResetPassword` and the old role-only view model.
- **Policy:** the class stays `[Authorize(Policy = AtsPermission.UsersManage)]`. Update the
  controller-authorization spot checks (`Edit` POST, `ResetPassword` POST, `Deactivate`).
- **e2e:** update `users.spec.ts` for the new UI (row Edit link, actions on the Edit page) and add:
  Owner edits a user's name and email, the user's open session is signed out, and they sign in with the
  new email; Owner's own Edit shows only Name as editable and no actions card.

- [x] **Step 1:** controller tests first (spot checks), then view models, controller, views.
- [x] **Step 2:** delete the old ResetPassword view; grep for links to `/Users/ResetPassword` GET.
- [x] **Step 3:** e2e changes.
- [x] **Step 4: Verify** build, tests, format, full Playwright.

---

### Task 4: Users list search and status filter

**Model:** opus (tenant-scoped query).

**Files:** `IUserRepository.cs`, `UserRepository.cs`, `UserService.cs`, fake + tests, `UsersController.Index`,
`Views/Users/Index.cshtml`, `tests/e2e/users.spec.ts` (or `smoke.spec.ts`).

- `ListAsync(UserStatusFilter status, string? q)`: `UserStatusFilter { Active, Deactivated, All }`,
  default **Active**. `q` matches display name or email (`EF.Functions.Like` with `%`, `_` and `[`
  escaped, or `Contains`, which EF parameterises safely; prefer `Contains`).
- Index view: search box and filter chips exactly like `Views/Jobs/Index.cshtml` (Active / Deactivated /
  All, keeping `q` across chips), eyebrow "N active:" unchanged, count on the right. Empty state when
  nothing matches.
- Tests: fake filtering is trivial, so add one service test that `ListAsync` passes the filter through,
  and an e2e check that the deactivated e2e users are hidden by default and shown under Deactivated.
- [x] Steps: tests first, implement, verify build/tests/format/Playwright.

---

### Task 5: UX fixes found in the browser pass (F3, F4, F5)

**Model:** opus (touches the audit query and the forced-change layout state).

1. **F3 Forced change shell.** When the signed-in user has the `must_change_password` claim, the
   back-office layout hides the sidebar nav links, the global search and the notifications button,
   leaving the brand, tenant card, user block with Sign out, and the page. Implement in the sidebar and
   top bar view components (they already read the user) rather than in each view. e2e: the held viewer
   in `users.spec.ts` sees no Jobs link and no search box until the password is changed.
2. **F4 Single-item row menu.** On `Views/Jobs/Index.cshtml`, render the row `...` menu only when the
   user has `jobs.manage`; otherwise leave the actions cell empty (the title already opens the board).
   Check `Views/Candidates/Index.cshtml` and `Views/Pipelines/Index.cshtml` for the same pattern and fix
   alike. e2e: extend `users.spec.ts` viewer test: no "Actions" button on the Jobs list.
3. **F5 Audit search.** `src/Ats.Infrastructure/Auditing/AuditQuery.cs` `SearchAsync`: also match
   `Action` and `EntityType`, and escape `%`, `_` and `[` in `q` before building the `LIKE` pattern
   (or switch to `Contains`). Add a unit test if the escaping is a pure helper; otherwise an e2e check
   that searching "User" on `/Audit` returns user entries.
- [x] Steps: one sub-step per item, verify build/tests/format/Playwright.

---

### Task 6: Documentation

**Model:** sonnet.

- Authorization skill: Users screen is now list (search + status filter) + one Edit page; the update
  rules table; own-record edit is name only; email change signs the user out; no password minimum (for now).
- RBAC spec: phase 2 addendum dated 1 October 2026 with the decisions above.
- UI skill: the Edit-page-with-actions pattern and the forced-change shell, if the skill documents
  page patterns.
- Tick this plan's checkboxes.

---

## Additions during implementation
Added beyond the tasks above (the Users paging item under "Out of scope" was reversed):
- **Paging** on the Users list (20 per page) through `IUserListQuery`, like Jobs and Candidates.
- **Pager bug fix:** `_Pager` dropped the page number from its links on every paged screen; the page is now
  kept in the route data.
- **Page clamp:** `Paging.Offset` and `Paging.Clamp` in `Ats.Application.Common`, used by every list query
  (jobs, candidates, users, audit, delivery log), so out-of-range pages land on the first or last page.
- **`LikePattern`:** SQL Server `LIKE` escaping (`%`, `_`, `[`) shared by the list queries, audit search and
  global search.
- **Layout-audit fixes and gates:** 44px phone tap targets, truncation, `.ats-form-grid`, dashboard card
  stretch, 4px spacing scale, `.ats-search--md`; locked by `layout-gates.spec.ts`.
- **Phone navigation:** Bootstrap offcanvas app bar, one-row phone top bar with a search toggle, Ctrl/Cmd+K
  opens it; locked by `mobile-nav.spec.ts`.
- **Compact phone cards** (`.ats-meta`) for Jobs, Candidates and Users.
- **Security card** on the Owner's own Edit page (link to Change password).
- New e2e specs `audit.spec.ts`, `mobile-nav.spec.ts`, `layout-gates.spec.ts` and `users.ts` helpers.

## Definition of done
- `dotnet build Ats.slnx`: 0 errors, no new warnings
- `dotnet test Ats.slnx`: green
- `dotnet format Ats.slnx --verify-no-changes`: clean
- `npx playwright test`: green (LocalDB started from the agent session: stop any instance running in
  another session first, as done on 1 October 2026)
- No migration
