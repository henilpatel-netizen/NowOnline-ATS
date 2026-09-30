# Ats Role-Based Access Control (RBAC) Design

Date: 30 September 2026. Status: phase 1 implemented.

## Permission matrix

| Permission | Constant | Owner | Recruiter | HiringManager | Viewer |
|---|---|---|---|---|---|
| `dashboard.view` | `DashboardView` | yes | yes | yes | yes |
| `jobs.view` | `JobsView` | yes | yes | yes (own jobs: phase 3) | yes |
| `jobs.manage` | `JobsManage` | yes | yes | no | no |
| `candidates.view` | `CandidatesView` | yes | yes | yes (own jobs: phase 3) | yes |
| `candidates.manage` | `CandidatesManage` | yes | yes | no | no |
| `applications.move` | `ApplicationsMove` | yes | yes | yes (own jobs: phase 3) | no |
| `resumes.download` | `ResumesDownload` | yes | yes | yes (own jobs: phase 3) | no |
| `pipelines.manage` | `PipelinesManage` | yes | yes | no | no |
| `organisation.manage` | `OrganisationManage` | yes | yes | no | no |
| `careersite.manage` | `CareerSiteManage` | yes | no | no | no |
| `integration.manage` | `IntegrationManage` | yes | no | no | no |
| `audit.view` | `AuditView` | yes | no | no | no |
| `users.manage` | `UsersManage` | yes | no | no | no |

## Roles
- **Owner:** tenant admin (users, integration, career site, audit, all data). Multiple Owners are allowed; there is no separate Admin role.
- **Recruiter:** day-to-day hiring plus pipeline and organisation setup.
- **HiringManager:** only assigned jobs and their candidates (scoping arrives in phase 3).
- **Viewer:** read-only, no CV download (GDPR data minimisation).

## Architecture
- Permissions are string constants in `Ats.Domain/Authorization` (`AtsPermission`).
- A static `RolePermissions` map groups them into the four roles.
- `Ats.Web` registers one ASP.NET Core authorization policy per permission
  (`RequireRole(RolePermissions.RolesWith(p))`) plus a require-authenticated fallback policy
  (`Ats.Web/Identity/PermissionPolicies.cs`).
- Controllers use `[Authorize(Policy = AtsPermission.X)]`; views use `User.Can(AtsPermission.X)`.
- "Own jobs" scoping (phase 3) goes in the `*ListQuery` read models, not in the tenancy global query filter.
- A future `Ats.Api` host reuses the same policies. The tenant always comes from the `tenant_id` claim.

## Phases
1. **Permission enforcement** (done, this spec). No migration.
2. **User management:** invite, change role, deactivate, last-Owner guard, `SecurityStamp` session
   revocation, audit entries. Must NOT allow assigning HiringManager before phase 3.
3. **HiringManager scoping:** `Job.HiringManagerUserId`; scope jobs, candidates, board, CVs, search and dashboard.
4. **`Ats.Api` host** with JWT, reusing the policies. Pending decisions:
   - API purpose: front end/mobile, or machine-to-machine API clients with scoped, hashed keys.
   - Token issuer: self-issued JWT behind `IIdentityService`, or Entra External ID.

## Decisions
- A single role per user (`AppUser.Role`) replaces the `UserRole` table mentioned in
  `docs/specs/2026-06-26-ats-product-design.md`.
- Denied signed-in users get a 403 rendered by the status page (`HomeController.Status`), not a redirect to sign-in.

## Known behaviour
- With the fallback policy, any endpoint without authorization metadata requires sign-in.
  Public endpoints need an explicit `[AllowAnonymous]`.
