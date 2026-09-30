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

## Known limits
- HiringManager is unscoped until phase 3 ("own jobs"). Do not make the role assignable before then.
- A role change applies at the user's next sign-in until phase 2 adds `SecurityStamp` validation.
- `User.Can` reads the single role claim and the role map directly; if policies gain extra requirements or users get multiple roles, switch views to `IAuthorizationService`.
