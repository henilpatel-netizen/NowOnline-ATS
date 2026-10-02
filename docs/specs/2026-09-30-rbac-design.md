# Ats Role-Based Access Control (RBAC) Design

Date: 30 September 2026. Status: phases 1, 2 and 3 implemented.

## Permission matrix

| Permission | Constant | Owner | Recruiter | HiringManager | Viewer |
|---|---|---|---|---|---|
| `dashboard.view` | `DashboardView` | yes | yes | yes | yes |
| `jobs.view` | `JobsView` | yes | yes | yes (own jobs) | yes |
| `jobs.viewall` | `JobsViewAll` | yes | yes | no | yes |
| `jobs.manage` | `JobsManage` | yes | yes | no | no |
| `candidates.view` | `CandidatesView` | yes | yes | yes (own jobs) | yes |
| `candidates.manage` | `CandidatesManage` | yes | yes | no | no |
| `applications.move` | `ApplicationsMove` | yes | yes | yes (own jobs) | no |
| `resumes.download` | `ResumesDownload` | yes | yes | yes (own jobs) | no |
| `pipelines.manage` | `PipelinesManage` | yes | yes | no | no |
| `organisation.manage` | `OrganisationManage` | yes | yes | no | no |
| `careersite.manage` | `CareerSiteManage` | yes | no | no | no |
| `integration.manage` | `IntegrationManage` | yes | no | no | no |
| `audit.view` | `AuditView` | yes | no | no | no |
| `users.manage` | `UsersManage` | yes | no | no | no |
| `profile.manage` | `ProfileManage` | yes | yes | yes | yes |

## Roles
- **Owner:** tenant admin (users, integration, career site, audit, all data). Multiple Owners are allowed; there is no separate Admin role.
- **Recruiter:** day-to-day hiring plus pipeline and organisation setup.
- **HiringManager:** only assigned jobs and their applications, candidates and CVs (phase 3). Assignable on the
  Users screen (label "Hiring manager").
- **Viewer:** read-only, no CV download (GDPR data minimisation).

## Architecture
- Permissions are string constants in `Ats.Domain/Authorization` (`AtsPermission`).
- A static `RolePermissions` map groups them into the four roles.
- `Ats.Web` registers one ASP.NET Core authorization policy per permission
  (`RequireRole(RolePermissions.RolesWith(p))`) plus a require-authenticated fallback policy
  (`Ats.Web/Identity/PermissionPolicies.cs`).
- Controllers use `[Authorize(Policy = AtsPermission.X)]`; views use `User.Can(AtsPermission.X)`.
- "Own jobs" scoping (phase 3) is explicit: `IJobScope` plus `JobScopeFilter` in the read models and by-id guards,
  not the tenancy global query filter. A user is scoped when they hold `jobs.view` without `jobs.viewall`.
- A future `Ats.Api` host reuses the same policies. The tenant always comes from the `tenant_id` claim.

## Phases
1. **Permission enforcement** (done, this spec). No migration.
2. **User management** (done, 30 September 2026): add user, change role, reset password, deactivate and
   reactivate, last-Owner guard, `SecurityStamp` session revocation, audit entries. HiringManager was not
   assignable until phase 3 (`AtsRole.Assignable`).
   - Decision (30 September 2026): an Owner sets a temporary password and the user must change it at first
     sign-in. Email invites follow when email is integrated.
   - Passwords (temporary and own): required, maximum 128 characters (hashing very long inputs is a
     denial-of-service risk). Decision (1 October 2026): the 12-character minimum is removed for now, to
     be reinstated later.
   - Last-Owner race: prevented by a serialisable transaction around the check and save, plus the
     `Users(TenantId, Role, IsActive)` index so range locks stay inside one tenant.
   - Accepted: an Owner can reset another Owner's password, and so sign in as them; the reset is audited
     (`UserPasswordReset`).
   - Deactivation (not deletion) is the only removal; it keeps audit history and authorship intact.
   - Out of scope: minimum password length on tenant sign-up (`Register`), throttling wrong
     current-password attempts.
3. **HiringManager scoping** (done, 2 October 2026). Plan: `docs/plans/2026-10-02-rbac-phase-3-hiring-manager-scoping.md`.
   The `JobHiringManager` table (migration `AddJobHiringManagers`) links jobs to hiring managers; it replaces the
   earlier idea of a single `Job.HiringManagerUserId`. Jobs, candidates, board, CVs, search, dashboard and the
   sidebar badges are scoped; out-of-scope ids return 404; a board move must match the route job for every role.
   HiringManager is assignable.
   - Decision (2 October 2026): several hiring managers per job.
   - Decision (2 October 2026): the HiringManager dashboard shows only their own jobs, and the activity feed
     (audit data) is hidden for them.
4. **`Ats.Api` host** with JWT, reusing the policies. Pending decisions:
   - API purpose: front end/mobile, or machine-to-machine API clients with scoped, hashed keys.
   - Token issuer: self-issued JWT behind `IIdentityService`, or Entra External ID.

## Addendum: phase 2 user editing (1 October 2026)
Plan: `docs/plans/2026-10-01-users-edit-page-and-ux-fixes.md`. Decisions by the developer:
- No password minimum for now (required, maximum 128 characters); to be reinstated later.
- An Owner can edit another user's name, email and role on one Edit page, which also holds reset password,
  deactivate and reactivate. The row menu and the separate Change role and Reset password pages are gone.
- On their own record an Owner can change the name only. Email and role changes on yourself are refused by
  `UserService`, not just hidden; the own password stays on Change password.
- Changing a user's email or role rotates their `SecurityStamp`, so they are signed out and sign in again
  (with the new email, if it changed). A name-only change does not.
- Email stays globally unique; the check runs only when the email changed, plus the unique-index violation
  on save.
- The audit action `UserRoleChanged` is replaced by `UserUpdated`, which lists what changed.
- The Users list gained search (name or email), an Active (default) / Deactivated / All filter and paging.

## Decisions
- A single role per user (`AppUser.Role`) replaces the `UserRole` table mentioned in
  `docs/specs/2026-06-26-ats-product-design.md`.
- Denied signed-in users get a 403 rendered by the status page (`HomeController.Status`), not a redirect to sign-in.

## Known behaviour
- With the fallback policy, any endpoint without authorization metadata requires sign-in.
  Public endpoints need an explicit `[AllowAnonymous]`.
