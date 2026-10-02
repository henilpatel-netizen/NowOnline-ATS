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

## Job scoping (HiringManager)
Phase 3. A HiringManager sees and acts only on the jobs they are assigned to, and on those jobs' applications,
candidates and CVs. Scoping is separate from tenancy (no global filter on `Job`; see the multitenancy skill).

| Role | `jobs.view` | `jobs.viewall` | Effect |
|---|---|---|---|
| Owner, Recruiter, Viewer | yes | yes | every job in the tenant |
| HiringManager | yes | no | assigned jobs only |

- **Model:** `JobHiringManager` (`TenantEntity`, unique `(TenantId, JobId, UserId)`; details in the entities skill).
  Several managers per job. A job's team is edited on the job form, which needs `jobs.manage`.
- **`IJobScope`** (`Ats.Application/Jobs`, impl `JobScope`, scoped): `IsRestricted` is true only for an authenticated
  user with `jobs.view` and without `jobs.viewall`; decided by permission, never role name. `UserId` is the user id
  while restricted, else null. Anonymous callers (career site, worker) are unrestricted: they never reach the scoped
  paths. A restricted user without a user id sees nothing.
- **Own data:** own jobs = jobs the user is on the team of (soft-deleted jobs stay hidden); own applications = those
  on an own job; own candidates = candidates with at least one own-job application. Candidate rows show only data
  from own-job applications (latest job, stage, count).
- **404 rule:** an out-of-scope id gets the same not-found as a missing id (no existence oracle): board, job edit,
  application details and card, candidate page, CV download. Services return null (or an empty list for the per-job
  readers), controllers map that to `NotFound()`.
- **Board move:** `IApplicationService.MoveStageAsync(jobId, applicationId, ...)` requires the application to be in
  scope AND to belong to the route `jobId`. This applies to every role (it also closed a gap where the route job id
  was never checked).
- **Where scoping lives:**
  - `JobScopeFilter` (`Ats.Application/Jobs`, plain LINQ, unit-tested like `UserListFilter`): `AssignedTo(userId)`,
    `VisibleTo(scope)` for jobs, applications, candidates and `ApplicationEvent`s (events are scoped through the
    visible applications), and `AllowsAsync(isOwn)` for by-id paths.
    Applications and candidates are scoped by joining through `db.Jobs`, never `JobHiringManagers` alone, so the
    soft-delete filter applies.
  - By-id guards: `JobService.GetAsync`, `JobService.UpdateAsync` (its own `AllowsAsync` check, same "Job not found."
    as a missing id, so editing does not rely on `jobs.manage` implying `jobs.viewall`; no extra query for an
    unrestricted user), `CandidateService.GetAsync`, and every per-job reader on
    `IApplicationService` (`GetJobAsync`, `GetStagesForJobAsync`, `ListForJobAsync`, `LatestEventTimesForJobAsync`,
    `GetAsync`, `GetWithCandidateAsync`, `ListEventsAsync`, `MoveStageAsync`) guard themselves.
  - Scoped read models: `JobListQuery`, `CandidateListQuery`, `ApplicationCardQuery`, `GlobalSearchService`,
    `DashboardService` (metrics, charts, attention items; for scoped users outbox counts are 0, settings are null and
    the activity feed is null),
    `ShellSummaryService` (sidebar badges, attention bell; failed deliveries and integration state are 0 or absent for
    scoped users).
  - Manage paths (`jobs.manage`, `candidates.manage`) are unscoped on purpose. The guard test
    `Every_role_that_manages_jobs_or_candidates_views_all_jobs` fails if a role with either gains manage without
    `jobs.viewall`.
- **Pickers:** the board add-candidate list and the candidates add-to-job list load only with `candidates.manage`
  (they list tenant-wide data).
- **Assignment:** `JobInput.HiringManagerIds` replaces the whole team. Every id must be an active HiringManager in the
  tenant, checked in one tenant-filtered query; any failure returns one generic error (no oracle on which ids exist).
  Duplicates are ignored. Links are added through `Job.HiringManagers` (TenantId from the interceptor) and removed by
  comparing against the loaded collection, never a posted id. `IJobRepository.TrySaveTeamChangesAsync` (edit) translates only
  the team unique-index violation; `TrySaveNewJobAsync` (create) translates only the job-number race
  (`IX_Jobs_TenantId_ExternalRef`), since a new job's team links cannot collide. Anything else is rethrown. The form shows chips (name and email); a member who was since deactivated or
  re-roled is shown but no longer assignable. The board header shows the team; Users Edit has an "Assigned jobs" card
  for a HiringManager.
- **Role change away from HiringManager** (`UserService.UpdateAsync`, inside its serialisable transaction): the user's
  `JobHiringManager` links are removed (`IUserRepository.RemoveHiringTeamLinksAsync`, tracked removal on the
  tenant-filtered set), so switching back later does not silently restore old assignments.
  `UserUpdateResult.RemovedFromJobs` carries the count of those jobs that are not deleted (links on deleted jobs are
  removed too) into the flash and the `UserUpdated` audit ("removed from 3
  hiring teams"). Deactivation keeps the links (a deactivated user cannot sign in; reactivation restores them).
- **CVs are stored per candidate** (`Candidate.ResumeFileKey`), not per application, so a manager on job A can download
  a CV the candidate uploaded when applying for job B. Accepted: it matches "own candidates' CVs".
- **Adding a new read path safely:** any new query or by-id method that a `jobs.view` user can reach over jobs,
  applications or candidates must apply `JobScopeFilter.VisibleTo` (lists, counts) or `AllowsAsync` (by id), and
  return not-found for out-of-scope ids. Pass `db.Jobs` as the jobs queryable. Add a test for the restricted user.
- The board header shows a member who is deactivated or no longer a HiringManager muted, with a "No access" text flag.
- E2E: `tests/e2e/hiring-manager.spec.ts`.

## Users and sessions
- `profile.manage` (all roles): own password change (`ProfileController`). `users.manage` (Owner): `UsersController`.
- Assignable roles: `AtsRole.Assignable` = Owner, Recruiter, HiringManager, Viewer (select label "Hiring manager",
  `RoleOptions.Label`). Job scoping is decided by permission: `jobs.viewall` (Owner, Recruiter, Viewer) sees every
  job; `jobs.view` without it (HiringManager) is limited to assigned jobs.
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
- `User.Can` reads the single role claim and the role map directly; if policies gain extra requirements or users get multiple roles, switch views to `IAuthorizationService`.
