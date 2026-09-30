# RBAC Phase 1: Permission Enforcement Implementation Plan

> **For agentic workers:** run through `/ats-ship docs/plans/2026-09-30-rbac-phase-1-permission-enforcement.md`.
> Project overrides apply: implementers never commit, never stage; they leave a working-tree diff.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce the approved role/permission matrix on every back-office endpoint and hide controls a
role cannot use, so a Viewer can no longer create, edit, move or download.

**Architecture:** Permissions are string constants in `Ats.Domain/Authorization`; a static
`RolePermissions` map groups them into the four existing roles. `Ats.Web` registers one ASP.NET Core
authorization policy per permission (`RequireRole(RolePermissions.RolesWith(p))`) plus a
require-authenticated fallback policy. Controllers use `[Authorize(Policy = AtsPermission.X)]`; views
use `User.Can(AtsPermission.X)`. No database change, no new package.

**Tech Stack:** .NET 10 MVC, ASP.NET Core policy-based authorization, xUnit, Playwright.

---

## Approved matrix (source of truth for this phase)

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

## Out of scope (later phases)
- Phase 2: Users screen (invite, change role, deactivate, last-Owner guard, `SecurityStamp` session
  revocation). **Phase 2 must not let anyone assign `HiringManager` until phase 3 ships**, because
  phase 1 grants that role unscoped view/move/download. Today no such user can exist (users are only
  created at onboarding, as Owner).
- Phase 3: `Job.HiringManagerUserId` and "own jobs" scoping in list queries, board, search, CVs.
- Phase 4: `Ats.Api` host with JWT, reusing these policies.
- `ICurrentUser.HasPermission`: not needed until phase 3 puts checks in the Application layer.

## File map

| File | Change |
|---|---|
| `src/Ats.Domain/Authorization/AtsPermission.cs` | Create: permission constants + `All` |
| `src/Ats.Domain/Authorization/RolePermissions.cs` | Create: role -> permissions map, `Has`, `RolesWith` |
| `tests/Ats.Tests/Authorization/RolePermissionsTests.cs` | Create: matrix test |
| `src/Ats.Web/Identity/PermissionPolicies.cs` | Create: `AddAtsPermissionPolicies`, `ClaimsPrincipal.Can` |
| `src/Ats.Web/Program.cs` | Modify: register policies, 403 on access denied, anonymous health + static assets |
| `tests/Ats.Tests/Ats.Tests.csproj` | Modify: reference `Ats.Web` |
| `tests/Ats.Tests/Authorization/PermissionPolicyTests.cs` | Create: policy evaluation per role |
| `src/Ats.Web/Controllers/*.cs` | Modify: permission policies per action, `[AllowAnonymous]` on Account/Home |
| `tests/Ats.Tests/Authorization/ControllerAuthorizationTests.cs` | Create: coverage + key-action tests |
| `src/Ats.Web/Views/**` + `SidebarNavViewComponent.cs` | Modify: hide controls, read-only forms, read-only board |
| `tests/e2e/rbac.spec.ts` | Create: Owner not locked out, anonymous surface still works |
| `.claude/skills/authorization/SKILL.md`, `CLAUDE.md`, `docs/specs/2026-09-30-rbac-design.md` | Docs |

---

### Task 1: Permission catalogue and role map

**Files:**
- Create: `src/Ats.Domain/Authorization/AtsPermission.cs`
- Create: `src/Ats.Domain/Authorization/RolePermissions.cs`
- Test: `tests/Ats.Tests/Authorization/RolePermissionsTests.cs`

- [x] **Step 1: Write the failing test**

`tests/Ats.Tests/Authorization/RolePermissionsTests.cs`:
```csharp
using Ats.Domain.Authorization;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Authorization;

// The matrix is a security boundary: an accidental grant here opens an endpoint to a role. Each role's
// expected set is spelled out in full so any added or removed permission fails a test.
public class RolePermissionsTests
{
    private static readonly string[] ReadOnly =
        { AtsPermission.DashboardView, AtsPermission.JobsView, AtsPermission.CandidatesView };

    public static TheoryData<string, string[]> Expected => new()
    {
        { AtsRole.Owner, AtsPermission.All },
        { AtsRole.Recruiter, [.. ReadOnly, AtsPermission.JobsManage, AtsPermission.CandidatesManage,
            AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload,
            AtsPermission.PipelinesManage, AtsPermission.OrganisationManage] },
        { AtsRole.HiringManager, [.. ReadOnly, AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload] },
        { AtsRole.Viewer, ReadOnly },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Role_has_exactly_its_permissions(string role, string[] expected)
    {
        var actual = AtsPermission.All.Where(p => RolePermissions.Has(role, p)).Order();
        Assert.Equal(expected.Order(), actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("owner")]      // role names are case-sensitive; the claim is written from AtsRole
    [InlineData("SuperAdmin")]
    public void Unknown_role_has_nothing(string? role) =>
        Assert.DoesNotContain(AtsPermission.All, p => RolePermissions.Has(role, p));

    [Fact]
    public void Admin_only_permissions_resolve_to_owner_only()
    {
        foreach (var p in new[] { AtsPermission.IntegrationManage, AtsPermission.AuditView,
                     AtsPermission.UsersManage, AtsPermission.CareerSiteManage })
            Assert.Equal(new[] { AtsRole.Owner }, RolePermissions.RolesWith(p));
    }

    [Fact]
    public void Every_permission_is_granted_to_some_role() =>
        Assert.All(AtsPermission.All, p => Assert.NotEmpty(RolePermissions.RolesWith(p)));

    [Fact]
    public void Permission_names_are_unique() =>
        Assert.Equal(AtsPermission.All.Length, AtsPermission.All.Distinct().Count());
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~Ats.Tests.Authorization`
Expected: build FAIL, `The type or namespace name 'Authorization' does not exist in the namespace 'Ats.Domain'`.

- [x] **Step 3: Write the implementation**

`src/Ats.Domain/Authorization/AtsPermission.cs`:
```csharp
namespace Ats.Domain.Authorization;

// Policy names. Controllers and views check these, never role names.
public static class AtsPermission
{
    public const string DashboardView = "dashboard.view";
    public const string JobsView = "jobs.view";
    public const string JobsManage = "jobs.manage";
    public const string CandidatesView = "candidates.view";
    public const string CandidatesManage = "candidates.manage";
    public const string ApplicationsMove = "applications.move";
    public const string ResumesDownload = "resumes.download";
    public const string PipelinesManage = "pipelines.manage";
    public const string OrganisationManage = "organisation.manage";
    public const string CareerSiteManage = "careersite.manage";
    public const string IntegrationManage = "integration.manage";
    public const string AuditView = "audit.view";
    public const string UsersManage = "users.manage";

    public static readonly string[] All =
    {
        DashboardView, JobsView, JobsManage, CandidatesView, CandidatesManage, ApplicationsMove,
        ResumesDownload, PipelinesManage, OrganisationManage, CareerSiteManage, IntegrationManage,
        AuditView, UsersManage,
    };
}
```

`src/Ats.Domain/Authorization/RolePermissions.cs`:
```csharp
using Ats.Domain.Enums;

namespace Ats.Domain.Authorization;

public static class RolePermissions
{
    private static readonly string[] ReadOnly =
        { AtsPermission.DashboardView, AtsPermission.JobsView, AtsPermission.CandidatesView };

    private static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.Ordinal)
    {
        [AtsRole.Owner] = [.. AtsPermission.All],
        [AtsRole.Recruiter] =
        [
            .. ReadOnly, AtsPermission.JobsManage, AtsPermission.CandidatesManage, AtsPermission.ApplicationsMove,
            AtsPermission.ResumesDownload, AtsPermission.PipelinesManage, AtsPermission.OrganisationManage,
        ],
        // ponytail: unscoped until phase 3 adds "own jobs" filtering; no HiringManager user can exist before phase 2.
        [AtsRole.HiringManager] = [.. ReadOnly, AtsPermission.ApplicationsMove, AtsPermission.ResumesDownload],
        [AtsRole.Viewer] = [.. ReadOnly],
    };

    public static bool Has(string? role, string permission) =>
        role is not null && Map.TryGetValue(role, out var granted) && granted.Contains(permission);

    public static string[] RolesWith(string permission) =>
        Map.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key).ToArray();
}
```
`ReadOnly` must stay declared above `Map`: static initialisers run in textual order.

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~Ats.Tests.Authorization`
Expected: PASS, 11 tests.

- [x] **Step 5: Mutation check (new suite passed first time rule)**

Temporarily add `AtsPermission.JobsManage` to the Viewer entry, rerun, confirm `Role_has_exactly_its_permissions(Viewer)`
fails, then edit it back. Also run `dotnet build Ats.slnx` (no new warnings) and
`dotnet format Ats.slnx --verify-no-changes`. Leave the diff; do not commit.

---

### Task 2: Policies, fallback policy and 403 handling in Ats.Web

**Files:**
- Create: `src/Ats.Web/Identity/PermissionPolicies.cs`
- Modify: `src/Ats.Web/Program.cs` (auth block lines ~51-62, health checks ~88-96, `MapStaticAssets` ~117)
- Modify: `src/Ats.Web/Controllers/AccountController.cs`, `src/Ats.Web/Controllers/HomeController.cs`
- Modify: `tests/Ats.Tests/Ats.Tests.csproj`
- Test: `tests/Ats.Tests/Authorization/PermissionPolicyTests.cs`

- [x] **Step 1: Reference Ats.Web from the test project**

In `tests/Ats.Tests/Ats.Tests.csproj`, next to the existing project references:
```xml
<ProjectReference Include="../../src/Ats.Web/Ats.Web.csproj" />
```

- [x] **Step 2: Write the failing test**

`tests/Ats.Tests/Authorization/PermissionPolicyTests.cs`:
```csharp
using System.Security.Claims;
using Ats.Domain.Authorization;
using Ats.Domain.Enums;
using Ats.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ats.Tests.Authorization;

// Proves the registered ASP.NET Core policies agree with RolePermissions for every role, i.e. that the
// map is actually what the framework enforces. This is the only per-role check until phase 2 lets the
// e2e suite sign in as non-Owner users.
public class PermissionPolicyTests
{
    private static readonly IAuthorizationService Auth = Build();

    private static IAuthorizationService Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(o => o.AddAtsPermissionPolicies());
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal SignedInAs(string role) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test"));

    [Fact]
    public async Task Every_role_and_permission_matches_the_map()
    {
        foreach (var role in AtsRole.All)
        foreach (var permission in AtsPermission.All)
        {
            var result = await Auth.AuthorizeAsync(SignedInAs(role), permission);
            Assert.True(result.Succeeded == RolePermissions.Has(role, permission), $"{role} / {permission}");
        }
    }

    [Fact]
    public async Task Anonymous_user_fails_every_permission()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        foreach (var permission in AtsPermission.All)
            Assert.False((await Auth.AuthorizeAsync(anonymous, permission)).Succeeded, permission);
    }

    [Fact]
    public void Fallback_policy_requires_an_authenticated_user()
    {
        var options = new AuthorizationOptions();
        options.AddAtsPermissionPolicies();
        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy!.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData(AtsRole.Viewer, AtsPermission.ResumesDownload, false)]
    [InlineData(AtsRole.Recruiter, AtsPermission.ResumesDownload, true)]
    public void Can_extension_reads_the_role_claim(string role, string permission, bool expected) =>
        Assert.Equal(expected, SignedInAs(role).Can(permission));
}
```

- [x] **Step 3: Run test to verify it fails**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~PermissionPolicyTests`
Expected: build FAIL, `The type or namespace name 'Identity' ... 'AddAtsPermissionPolicies'` not found.

- [x] **Step 4: Write the implementation**

`src/Ats.Web/Identity/PermissionPolicies.cs`:
```csharp
using System.Security.Claims;
using Ats.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Ats.Web.Identity;

public static class PermissionPolicies
{
    // One policy per permission, named after it. The fallback policy makes any endpoint without
    // authorization metadata require a signed-in user, so a new controller is never public by accident.
    public static void AddAtsPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in AtsPermission.All)
            options.AddPolicy(permission, p => p.RequireRole(RolePermissions.RolesWith(permission)));

        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }

    // For views and view components: hides what the policy would refuse anyway.
    public static bool Can(this ClaimsPrincipal user, string permission) =>
        RolePermissions.Has(user.FindFirst(ClaimTypes.Role)?.Value, permission);
}
```

`src/Ats.Web/Program.cs`, replace the cookie + authorization block:
```csharp
builder.Services.AddAuthentication("AtsCookie")
    .AddCookie("AtsCookie", o =>
    {
        o.LoginPath = "/Account/Login";
        // A signed-in user without the permission gets a 403, rendered by the status-code page
        // (HomeController.Status), instead of being bounced to the login page.
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o => o.AddAtsPermissionPolicies());
```
Add `using Ats.Web.Identity;` at the top if not present (and `using Microsoft.AspNetCore.Authorization;` for the next edit).

Health checks: append `.AllowAnonymous()` to each of the three `app.MapHealthChecks(...)` calls, e.g.
```csharp
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
```
Static assets (the login page and career site need CSS/JS before sign-in; the fallback policy would
otherwise cover these endpoints):
```csharp
app.MapStaticAssets().Add(b => b.Metadata.Add(new AllowAnonymousAttribute()));
```

`AccountController.cs`: add `using Microsoft.AspNetCore.Authorization;` and `[AllowAnonymous]` on the class.
`HomeController.cs`: add `using Microsoft.AspNetCore.Authorization;` and `[AllowAnonymous]` on the class
(`Index` only redirects to Dashboard, which is gated; `Error`/`Status` must render for anyone).

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ats.slnx`
Expected: all green, including the 4 new `PermissionPolicyTests` (5 cases).

- [x] **Step 6: Mutation check and hygiene**

Temporarily change `RequireRole(...)` to `RequireAuthenticatedUser()`, confirm
`Every_role_and_permission_matches_the_map` fails, edit it back. `dotnet build Ats.slnx` with no new
warnings; `dotnet format Ats.slnx --verify-no-changes` clean. Leave the diff; do not commit.

---

### Task 3: Apply permission policies to every controller action

**Files:**
- Modify: every controller in `src/Ats.Web/Controllers/` listed below
- Test: `tests/Ats.Tests/Authorization/ControllerAuthorizationTests.cs`

Rules: replace every bare `[Authorize]` and every `[Authorize(Roles = ...)]` with permission policies.
Multiple `[Authorize]` attributes on class and action are ANDed. Remove `using Ats.Domain.Enums;` where
it becomes unused; add `using Ats.Domain.Authorization;`.

| Controller | Class-level | Action-level |
|---|---|---|
| `DashboardController` | `DashboardView` | none |
| `JobsController` | `JobsView` | `JobsManage` on `Create` (GET+POST), `Edit` (POST only), `Publish`, `Close`, `Delete`. `Edit` GET stays `JobsView` (read-only view, Task 4) |
| `CandidatesController` | `CandidatesView` | `CandidatesManage` on `Create` (GET+POST), `Edit` (POST only), `Delete`, `AddToJob`. `Edit` GET stays view-only |
| `ApplicationsController` | `CandidatesView` | `CandidatesManage` on `Remove` |
| `BoardController` | `JobsView` and `CandidatesView` | `ApplicationsMove` on `Move`; `CandidatesManage` on `AddCandidate` |
| `ResumeController` | `ResumesDownload` | none |
| `PipelinesController` | `PipelinesManage` | none |
| `OrganisationController`, `DepartmentsController`, `LocationsController` | `OrganisationManage` | none |
| `CareerSiteController` | `JobsView` | `CareerSiteManage` on both `Branding` actions (replaces `Roles = AtsRole.Owner`) |
| `IntegrationController` | `IntegrationManage` | none |
| `AuditController` | `AuditView` | none |
| `SearchController` | `JobsView` and `CandidatesView` | none. Replace the stale comment (lines 7-9) with: `// Search spans jobs and candidates, so it needs both view permissions. Phase 3 scopes results for HiringManager.` |
| `AccountController`, `HomeController` | `[AllowAnonymous]` (Task 2) | none |

Example (`JobsController`):
```csharp
[Authorize(Policy = AtsPermission.JobsView)]
public class JobsController : Controller
{
    // ...
    [HttpGet]
    [Authorize(Policy = AtsPermission.JobsManage)]
    public async Task<IActionResult> Create() { /* unchanged */ }

    [HttpPost]
    [Authorize(Policy = AtsPermission.JobsManage)]
    public async Task<IActionResult> Create(JobEditViewModel vm) { /* unchanged */ }
```

- [x] **Step 1: Write the failing test**

`tests/Ats.Tests/Authorization/ControllerAuthorizationTests.cs`:
```csharp
using System.Reflection;
using Ats.Domain.Authorization;
using Ats.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ats.Tests.Authorization;

// Guards the controller surface: every action is either deliberately public or gated by a named
// permission policy. A bare [Authorize] or [Authorize(Roles = ...)] fails, so role checks cannot creep back.
public class ControllerAuthorizationTests
{
    private static readonly Type[] Controllers = typeof(HomeController).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToArray();

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

    private static List<object> Attributes(MethodInfo action) =>
        action.DeclaringType!.GetCustomAttributes(true).Concat(action.GetCustomAttributes(true)).ToList();

    [Fact]
    public void Every_action_is_anonymous_or_permission_gated()
    {
        var offenders = new List<string>();
        foreach (var c in Controllers)
        foreach (var a in Actions(c))
        {
            var attrs = Attributes(a);
            if (attrs.OfType<AllowAnonymousAttribute>().Any()) continue;
            var authorize = attrs.OfType<AuthorizeAttribute>().ToList();
            if (authorize.Count == 0 || authorize.Any(x => x.Policy is null || !AtsPermission.All.Contains(x.Policy)))
                offenders.Add($"{c.Name}.{a.Name}");
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_expected_controllers_are_anonymous()
    {
        var anonymous = Controllers.Where(c => c.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(c => c.FullName).Order();
        Assert.Equal(new[]
        {
            "Ats.Web.Areas.Careers.Controllers.JobsController",
            "Ats.Web.Controllers.AccountController",
            "Ats.Web.Controllers.HomeController",
        }, anonymous);
    }

    // Spot-checks that the matrix reached the actions that matter most.
    [Theory]
    [InlineData(typeof(JobsController), "Delete", true, AtsPermission.JobsManage)]
    [InlineData(typeof(JobsController), "Edit", true, AtsPermission.JobsManage)]
    [InlineData(typeof(JobsController), "Edit", false, AtsPermission.JobsView)]
    [InlineData(typeof(CandidatesController), "Delete", true, AtsPermission.CandidatesManage)]
    [InlineData(typeof(CandidatesController), "Edit", false, AtsPermission.CandidatesView)]
    [InlineData(typeof(BoardController), "Move", true, AtsPermission.ApplicationsMove)]
    [InlineData(typeof(ResumeController), "Download", false, AtsPermission.ResumesDownload)]
    [InlineData(typeof(IntegrationController), "Index", true, AtsPermission.IntegrationManage)]
    [InlineData(typeof(AuditController), "Index", false, AtsPermission.AuditView)]
    [InlineData(typeof(CareerSiteController), "Branding", true, AtsPermission.CareerSiteManage)]
    [InlineData(typeof(PipelinesController), "Save", true, AtsPermission.PipelinesManage)]
    [InlineData(typeof(DepartmentsController), "Delete", true, AtsPermission.OrganisationManage)]
    public void Action_requires_policy(Type controller, string action, bool post, string policy)
    {
        var method = Actions(controller).Single(m => m.Name == action &&
            (m.GetCustomAttribute<HttpPostAttribute>() is not null) == post);
        Assert.Contains(policy, Attributes(method).OfType<AuthorizeAttribute>().Select(a => a.Policy));
    }

    [Fact]
    public void Edit_get_is_not_gated_by_manage()
    {
        foreach (var (c, manage) in new[] { (typeof(JobsController), AtsPermission.JobsManage),
                     (typeof(CandidatesController), AtsPermission.CandidatesManage) })
        {
            var get = Actions(c).Single(m => m.Name == "Edit" && m.GetCustomAttribute<HttpPostAttribute>() is null);
            Assert.DoesNotContain(manage, Attributes(get).OfType<AuthorizeAttribute>().Select(a => a.Policy));
        }
    }
}
```
If `Single` throws for an action whose GET has no `[HttpGet]` attribute (for example `AuditController.Index`),
the `post: false` predicate still matches it because it only checks for `[HttpPost]`.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~ControllerAuthorizationTests`
Expected: FAIL. `Every_action_is_anonymous_or_permission_gated` lists offenders such as `JobsController.Index`,
`SearchController.Index`, `AuditController.Index`.

- [x] **Step 3: Apply the table above to each controller**

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ats.slnx`
Expected: all green.

- [x] **Step 5: Hygiene**

`dotnet build Ats.slnx` with no new warnings (watch for unused `using Ats.Domain.Enums;`);
`dotnet format Ats.slnx --verify-no-changes` clean. Leave the diff; do not commit.

---

### Task 4: Hide what a role cannot do (views, sidebar, board)

Read `.claude/skills/ui/SKILL.md` first. Server-side policies (Task 3) are the protection; this task
only stops the UI offering actions that would return 403.

**Files:**
- Modify: `src/Ats.Web/Views/_ViewImports.cshtml`
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`
- Modify: `src/Ats.Web/Views/Jobs/Index.cshtml`, `Jobs/Form.cshtml`
- Modify: `src/Ats.Web/Views/Candidates/Index.cshtml`, `Candidates/Form.cshtml`
- Modify: `src/Ats.Web/Views/Board/Index.cshtml`, `Board/_Board.cshtml`
- Modify: `src/Ats.Web/Views/Shared/Partials/_CandidateDrawer.cshtml`
- Modify: `src/Ats.Web/Views/CareerSite/Index.cshtml`
- Test: `tests/e2e/rbac.spec.ts`

- [x] **Step 1: Write the e2e spec (fails on the anonymous-asset and health cases only if Task 2 regressed; the Owner cases guard against locking the Owner out)**

`tests/e2e/rbac.spec.ts`:
```ts
import { test, expect } from '@playwright/test';

// Non-Owner roles cannot be signed in until phase 2 adds user management; their matrix is covered by
// tests/Ats.Tests/Authorization. This spec proves the Owner keeps full access and that the
// require-authenticated fallback policy did not break the public surface.

test.describe('owner', () => {
  test('sees admin navigation', async ({ page }) => {
    await page.goto('/Dashboard');
    const nav = page.locator('#ats-sidebar');
    for (const name of ['Pipelines', 'Organisation', 'Integrations', 'Audit log']) {
      await expect(nav.getByRole('link', { name, exact: false })).toBeVisible();
    }
  });

  test('sees manage actions', async ({ page }) => {
    await page.goto('/Jobs');
    await expect(page.getByRole('link', { name: 'New job' })).toBeVisible();
    await page.goto('/Candidates');
    await expect(page.getByRole('link', { name: 'Add candidate' })).toBeVisible();
    await page.goto('/CareerSite');
    await expect(page.getByRole('link', { name: 'Branding' })).toBeVisible();
  });

  test('can open the job form with editable fields', async ({ page }) => {
    await page.goto('/Jobs/Create');
    await expect(page.locator('#Title')).toBeEditable();
    await expect(page.getByRole('button', { name: 'Save' })).toBeVisible();
  });
});

test.describe('anonymous', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('back office redirects to sign-in', async ({ page }) => {
    await page.goto('/Jobs');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('sign-in page loads its stylesheets', async ({ page }) => {
    const failed: string[] = [];
    page.on('response', r => { if (r.url().endsWith('.css') && r.status() >= 400) failed.push(r.url()); });
    await page.goto('/Account/Login');
    await expect(page.locator('#Email')).toBeVisible();
    expect(failed).toEqual([]);
  });

  test('liveness probe is public', async ({ request }) => {
    const res = await request.get('/health/live');
    expect(res.status()).toBe(200);
  });
});
```

- [x] **Step 2: View imports**

`src/Ats.Web/Views/_ViewImports.cshtml`, add:
```cshtml
@using Ats.Domain.Authorization
@using Ats.Web.Identity
```

- [x] **Step 3: Sidebar uses permissions**

`SidebarNavViewComponent.cs`: rename the `NavItem` parameter `string? RequiredRole = null` to
`string? RequiredPermission = null`; replace `using Ats.Domain.Enums;` with
`using Ats.Domain.Authorization;` and `using Ats.Web.Identity;`. Items:
```csharp
new("Pipelines", "view_week", "Pipelines", "Index", NavGroup.Setup, AtsPermission.PipelinesManage),
new("Organisation", "apartment", "Organisation", "Index", NavGroup.Setup, AtsPermission.OrganisationManage),
new("Career site", "public", "CareerSite", "Index", NavGroup.Setup),

new("Integrations", "cable", "Integration", "Index", NavGroup.Admin, AtsPermission.IntegrationManage,
    Alert: s => s.IntegrationUnhealthy),
new("Audit log", "history", "Audit", "Index", NavGroup.Admin, AtsPermission.AuditView),
```
Filter in `InvokeAsync`:
```csharp
var groups = Items
    .Where(i => i.RequiredPermission is null || UserClaimsPrincipal.Can(i.RequiredPermission))
    .GroupBy(i => i.Group)
    .ToList();
```
Keep the `role` variable; the sidebar footer still displays it.

- [x] **Step 4: Jobs views**

`Jobs/Index.cshtml`: wrap the `PageActions` button:
```cshtml
@section PageActions {
    @if (User.Can(AtsPermission.JobsManage))
    {
        <a class="btn btn-primary" asp-action="Create"><span class="ms ms-sm">add</span> New job</a>
    }
}
```
In the row dropdown keep "Open board" for everyone; wrap the `Edit` item, the Publish/Close block, the
divider and the Delete item in `@if (User.Can(AtsPermission.JobsManage)) { ... }`.

`Jobs/Form.cshtml`: read-only for users without `jobs.manage` (the board's "Job details" tab links here):
```cshtml
@{
    var readOnly = Model.Id is not null && !User.Can(AtsPermission.JobsManage);
    ViewData["Title"] = Model.Id is null ? "New job" : readOnly ? "Job details" : "Edit job";
}
<form asp-action="@(Model.Id is null ? "Create" : "Edit")" method="post" class="col-lg-8">
    <fieldset disabled="@readOnly">
        ... all existing fields, unchanged ...
    </fieldset>
    <div class="d-flex align-items-center gap-2 border-top pt-3">
        @if (!readOnly) { <button type="submit" class="btn btn-primary">Save</button> }
        <a class="btn btn-outline-secondary" asp-action="Index">@(readOnly ? "Back" : "Cancel")</a>
    </div>
</form>
```
The `<fieldset>` wraps the validation summary, hidden `Id` and every field div; the button row moves
out of it. Bootstrap's reboot already strips fieldset border, padding and margin, so no extra class.

- [x] **Step 5: Candidates views**

`Candidates/Index.cshtml`: wrap the `PageActions` "Add candidate" link in
`@if (User.Can(AtsPermission.CandidatesManage))`. Wrap the whole row-actions `<div class="dropdown">`
in the same check (both its items, Add to job and Delete, are manage actions). Leave the empty
`<span class="d-flex justify-content-end ats-row-actions">` in place so the grid column still exists.

`Candidates/Form.cshtml`: same pattern as the job form:
```cshtml
@{
    var readOnly = Model.Id != 0 && !User.Can(AtsPermission.CandidatesManage);
    ViewData["Title"] = Model.Id == 0 ? "New candidate" : readOnly ? "Candidate" : "Edit candidate";
}
```
Wrap the validation summary, hidden `Id` and the four field divs in `<fieldset disabled="@readOnly">`;
show Save only when `!readOnly`; Cancel text becomes "Back" when read-only. Change the delete block's
condition to `@if (Model.Id != 0 && !readOnly)`.

- [x] **Step 6: Board**

`Board/Index.cshtml`: wrap the "Add candidate" toggle button and the whole `#add-candidate` collapse in
`@if (User.Can(AtsPermission.CandidatesManage))`. In the script, make drag and select depend on the
server-rendered flag:
```js
function initBoard() {
    const container = document.getElementById('board-container');
    const canMove = !!container && container.dataset.canMove === 'true';
    document.querySelectorAll('.ats-board-cards').forEach(function (el) {
        Sortable.create(el, {
            group: 'stages',
            animation: 120,
            disabled: !canMove,
            onEnd: function (evt) { /* unchanged */ }
        });
    });
    // ... the .move-select loop and drawer code stay unchanged ...
```
`Board/_Board.cshtml`: first lines become
```cshtml
@{ var canMove = User.Can(AtsPermission.ApplicationsMove); }
<div id="board-container" data-can-move="@(canMove ? "true" : "false")">
```
and wrap the `<select class="form-select form-select-sm move-select" ...>...</select>` in
`@if (canMove) { ... }`. The partial is also rendered by the `Move` POST response, which only a user
with `applications.move` can reach, so the flag stays correct after a swap.

- [x] **Step 7: Candidate drawer**

`Shared/Partials/_CandidateDrawer.cshtml`: wrap both Resume `Download` links (the "Download CV" button
and the icon link in the file card) in `User.Can(AtsPermission.ResumesDownload)` checks, combined with
the existing `ResumeFileName is not null` condition. Wrap the `Applications/Remove` form in
`@if (User.Can(AtsPermission.CandidatesManage))`.

- [x] **Step 8: Career site**

`CareerSite/Index.cshtml`: replace `@if (User.IsInRole(AtsRole.Owner))` with
`@if (User.Can(AtsPermission.CareerSiteManage))` and drop the now-unused `@using Ats.Domain.Enums` if
nothing else in the file needs it.

- [x] **Step 9: Verify**

Run: `dotnet build Ats.slnx` (no new warnings), `dotnet test Ats.slnx`, `dotnet format Ats.slnx --verify-no-changes`.
Run: `npx playwright test` (whole suite: the Owner journeys must be unchanged, and `rbac.spec.ts` green).
Expected: all pass. Leave the diff; do not commit.

---

### Task 5: Documentation

**Files:**
- Create: `.claude/skills/authorization/SKILL.md`
- Create: `docs/specs/2026-09-30-rbac-design.md`
- Modify: `CLAUDE.md`

- [x] **Step 1: Skill file**

`.claude/skills/authorization/SKILL.md`:
```markdown
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
  `ControllerAuthorizationTests` fails the build's test run.
- Public endpoints need an explicit `[AllowAnonymous]` (Account, Home, Careers area, health checks,
  static assets). Adding one means updating `Only_the_expected_controllers_are_anonymous`.
- Denied signed-in users get a 403 rendered by `HomeController.Status`; boosted requests hard-navigate to it.

## Views
- `User.Can(AtsPermission.X)` (in `_ViewImports`) hides controls; the policy is the real protection.
- Detail pages without manage permission render read-only: `<fieldset disabled="@readOnly">`, no Save,
  no Delete (see `Jobs/Form.cshtml`, `Candidates/Form.cshtml`). The board reads `data-can-move`.
- Sidebar entries take `RequiredPermission`.

## Adding a permission
1. Constant + `All` in `AtsPermission`. 2. Grant it in `RolePermissions`. 3. Update the expected sets in
`RolePermissionsTests`. 4. Gate the action and the control.

## Known limits
- HiringManager is unscoped until phase 3 ("own jobs"). Do not make the role assignable before then.
- A role change applies at the user's next sign-in until phase 2 adds `SecurityStamp` validation.
```

- [x] **Step 2: Spec**

`docs/specs/2026-09-30-rbac-design.md`: the matrix table from the top of this plan, the four roles with
one-line descriptions (Owner: tenant admin; Recruiter: day-to-day hiring and setup; HiringManager:
assigned jobs only; Viewer: read-only, no CV download), the architecture paragraph from this plan's
header, and the four phases with their "Out of scope" notes (including: Phase 2 must not allow assigning
HiringManager before phase 3; single role per user replaces the product design's `UserRole` table;
Phase 4 API decisions pending: API purpose and token issuer).

- [x] **Step 3: CLAUDE.md**

Add a row to the skill-index table:
```markdown
| Authorization | `.claude/skills/authorization/SKILL.md` | Permissions, role map, policies, fallback policy, gating actions and controls |
```
Add under Conventions:
```markdown
- **Authorization is permission-based.** Gate actions with `[Authorize(Policy = AtsPermission.X)]` and
  controls with `User.Can(AtsPermission.X)`; never role names. A fallback policy requires sign-in, so
  public endpoints need `[AllowAnonymous]`. Details: authorization skill.
```

- [x] **Step 4: Verify**

`dotnet format Ats.slnx --verify-no-changes` still clean (docs only). Leave the diff; do not commit.

---

## Definition of done (phase)
- `dotnet build Ats.slnx`: 0 errors, no new warnings
- `dotnet test Ats.slnx`: green, including `Ats.Tests.Authorization`
- `dotnet format Ats.slnx --verify-no-changes`: clean
- `npx playwright test`: green
- No migration in this phase
