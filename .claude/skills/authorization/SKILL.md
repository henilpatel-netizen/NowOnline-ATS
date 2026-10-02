---
name: authorization
description: Ats role-based access control - permission constants, the role map, policy registration, the fallback policy, and how to gate a new action or view control. Read before adding a controller, action, or any button that changes data.
---

# Authorization (RBAC)

## Model
- Permissions: `Ats.Domain/Authorization/AtsPermission.cs` (`jobs.manage`, ...). Check these, never role names.
- Roles -> permissions: `RolePermissions.cs`, a static map. Roles are `AtsRole.*`, one per user (`AppUser.Role`),
  carried as the `ClaimTypes.Role` cookie claim.
- Matrix and phase plan: `docs/specs/2026-09-30-rbac-design.md`.

## Enforcement
- `Ats.Web/Identity/PermissionPolicies.cs` registers one policy per permission (policy name = permission)
  and a require-authenticated **fallback policy**: an endpoint with no metadata needs a signed-in user.
- Controllers: `[Authorize(Policy = AtsPermission.X)]` at class level for the view permission, plus
  action level for manage/move. Attributes are ANDed. Never `[Authorize]` bare or `Roles = ...`:
  `ControllerAuthorizationTests` fails `dotnet test`.
- Public endpoints need an explicit `[AllowAnonymous]` (Account, Home, Careers area, health checks,
  static assets). Adding one means updating `Only_the_expected_controllers_are_anonymous`.
- Denied signed-in users get a 403 rendered by `HomeController.Status`; boosted requests hard-navigate to it.

## Views
- `User.Can(AtsPermission.X)` (in `_ViewImports`) hides controls; the policy is the real protection.
- Detail pages without manage permission render read-only: `<fieldset disabled="@readOnly">`, no Save,
  no Delete (see `Jobs/Form.cshtml`, `Candidates/Form.cshtml`). The board reads `data-can-move`.
- Sidebar entries and dashboard `AttentionItem`s take `RequiredPermission`; the view drops those the user cannot open.

## Adding a permission
1. Constant + `All` in `AtsPermission`. 2. Grant it in `RolePermissions`. 3. Update the expected sets in
`RolePermissionsTests`. 4. Gate the action and the control.

## Users and sessions
- `profile.manage` (all roles): own password change (`ProfileController`). `users.manage` (Owner): `UsersController`.
- Assignable roles: `AtsRole.Assignable` = Owner, Recruiter, Viewer. HiringManager is withheld until phase 3.
- `UserService` rules: email trimmed, lower-cased, valid, globally unique (`IOnboardingStore.EmailExistsAsync`
  plus the `IX_Users_Email` violation translated in `UserRepository.TryAddAsync` on create and
  `TrySaveAsync` on update); name required, max 200;
  passwords required (not blank or whitespace-only), max 128 characters (`UserService.MaxPasswordLength`;
  hashing very long inputs is a DoS risk), no minimum length (removed by decision on 1 October 2026, to be
  reinstated later); a new password must differ from the current one; new and reset users must change their
  password; no self deactivate or self admin reset; an admin password reset of a deactivated user is refused
  ("Reactivate the user first."). Deactivation is the only removal.
- Users screen (`UsersController`, `users.manage`):
  - **List** (`Users/Index`): read model `IUserListQuery` (`*ListQuery` convention). Search `q` matches name or
    email (`LikePattern`), status chips `UserStatusFilter` Active (default) / Deactivated / All, paged 20
    (`Paging.Clamp`). Each row has one Edit icon; the name is the row link. Create is a separate page.
  - **Edit** (`GET/POST Users/Edit/{id}`): header card (avatar, status, role, added), Details card (name, email,
    role), then for another user a **Danger zone** (reset password, deactivate) when active, or **Account
    access** (reactivate) when deactivated. Details stay editable when deactivated. On your own record the
    Details card edits the **name only** (email and role shown in a `<dl>`) and a **Security** card links to
    Change password. Every POST redirects back to Edit (PRG); flashes name the user. A reset validation error
    travels in `TempData["Error"]`, never the password.
  - **Update rules** (`UserService.UpdateAsync(UpdateUserInput, actingUserId)` returns `UserUpdateResult`:
    `Result`, `ChangedFields` of `UserField`, `SignedOut`):

    | Rule | Detail |
    |---|---|
    | Self | Only the name may change; a changed email or role returns "You can only change your own name here." and saves nothing |
    | Email | Uniqueness is checked only when the normalised email changed; a unique-index race on save gives the same duplicate message |
    | Role | Must be in `AtsRole.Assignable` (a stored role outside it passes while unchanged); demoting the last active Owner fails |
    | Stamp | `SecurityStamp` rotates when email or role changed (`SignedOut`); a name-only change does not |
    | No-op | Nothing changed: `Ok`, no save, no rotation; the controller flashes "No changes to save." and audits nothing |
    | Transaction | Load, checks and save run in one serialisable `InTransactionAsync` |

  - `SetActiveAsync` returns `UserActiveResult(Result, Changed)`; setting the state a user already has is a
    no-op (no save, no stamp rotation, no audit). `ChangeRoleAsync` no longer exists.
  - An own-name change re-issues the cookie (`IIdentityService.GetSessionAsync` + `AtsSignIn`) so the sidebar
    shows the new name; it does not rotate the stamp.
  - A name-only change by another Owner does not rotate the stamp either, so that user's sidebar shows the old
    name until their next sign-in (by design).
- Accepted behaviour: an Owner can reset another Owner's password (and so sign in as them). It is not
  blocked; the reset is audited as `UserPasswordReset`.
- Last Owner: at least one active Owner must remain. The check and save run in a SERIALIZABLE transaction
  (`UserRepository.InTransactionAsync`, inside the EF execution strategy). Concurrent demotions deadlock and
  the retry sees one Owner and fails. The `Users(TenantId, Role, IsActive)` index keeps the range locks inside one tenant.
- `AppUser.SecurityStamp` rotates on every role or email change, deactivate, reactivate (when the state
  actually changes), admin reset and own password change (`UserService`). A changed email signs the user out;
  they sign in again with the new address. `SessionValidator` (cookie `OnValidatePrincipal`) checks stamp, user active and
  tenant active on every request except `[AllowAnonymous]` endpoints; a mismatch rejects and signs out.
  It reads through `IdentityService.GetSessionAsync(userId, tenantId)` (documented filter bypass).
- `AtsSignIn` is the only way to issue the cookie (claims: NameIdentifier, Name, Role, `tenant_id`,
  `security_stamp`, `must_change_password`). `ValidateCredentialsAsync` rejects inactive users.
- `MustChangePassword`: `RequirePasswordChangeFilter` holds the user on `/Profile/ChangePassword`. Exempt:
  Profile, Account, Home and any area. Non-boosted htmx requests get `HX-Redirect`. While held, the sidebar
  has no nav links and the top bar has no search or notifications (`SidebarNavViewComponent`,
  `TopBarViewComponent`); the brand, user block and Sign out stay.
- Audit: UserCreated, UserUpdated, UserPasswordReset, UserDeactivated, UserReactivated, PasswordChanged
  (details in the audit skill). Audit summaries never contain passwords; update, reset and activation
  entries describe the user from the stored record (`UserAuditSummary` for updates).

## Known limits
- HiringManager is not assignable (`AtsRole.Assignable`) until phase 3 scopes it.
- `User.Can` reads the single role claim and the role map directly; if policies gain extra requirements or users get multiple roles, switch views to `IAuthorizationService`.
