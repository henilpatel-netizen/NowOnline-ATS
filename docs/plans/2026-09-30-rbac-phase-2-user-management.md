# RBAC Phase 2: User Management Implementation Plan

> **For agentic workers:** run through `/ats-ship docs/plans/2026-09-30-rbac-phase-2-user-management.md`.
> Project overrides apply: implementers never commit or stage; they leave a working-tree diff.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Owners can add team members with a role and a temporary password, change their role,
reset their password and deactivate them; changes take effect on the user's next request.

**Architecture:** `AppUser` gains `IsActive`, `MustChangePassword` and `SecurityStamp`. A new
Application `UserService` (hand-rolled fakes in tests) owns every rule: assignable roles, global email
uniqueness (reusing `IOnboardingStore.EmailExistsAsync`), minimum password length, no self-demotion,
at least one active Owner, and a new `SecurityStamp` on every security-relevant change. The cookie
`OnValidatePrincipal` event compares the cookie's stamp with the database on every non-anonymous
request, so a role change, deactivation or password reset signs the user out at once. Users created or
reset with a temporary password are held on `/Profile/ChangePassword` by a global action filter until
they set their own.

**Tech Stack:** .NET 10 MVC, EF Core (SQL Server), ASP.NET Core cookie auth, xUnit, Playwright.

**Decision recorded:** temporary password set by the Owner (chosen 30 September 2026). Email invites
replace it when email is integrated; `MustChangePassword` stays useful for that flow too.

---

## Rules (source of truth for this phase)

| Rule | Where enforced |
|---|---|
| Assignable roles: Owner, Recruiter, Viewer. **HiringManager is not assignable** until phase 3 scopes it | `AtsRole.Assignable`, `UserService` |
| Email trimmed, lower-cased, valid, globally unique (all tenants) | `UserService` via `IOnboardingStore.EmailExistsAsync` |
| Display name required, max 200 | `UserService` + view model |
| Temporary and new passwords: 12 to 128 characters | `UserService.MinPasswordLength` + view models |
| New users and reset users must change their password at next sign-in | `MustChangePassword`, `RequirePasswordChangeFilter` |
| An Owner cannot change their own role, deactivate or admin-reset themselves | `UserService` (self checks) |
| A tenant always keeps at least one **active** Owner | `UserService` (`CountActiveOwnersAsync`) |
| Role change, deactivate, reactivate, reset, own password change: new `SecurityStamp` | `UserService` |
| Stale stamp, inactive user or suspended tenant: cookie rejected on next request | `SessionValidator` |
| Inactive users cannot sign in | `IdentityService.ValidateCredentialsAsync` |
| Every admin action is audited; passwords never appear in audit text or logs | `UsersController`, `ProfileController` |

## Out of scope
- HiringManager assignment and "own jobs" scoping: phase 3.
- Email invites / forgotten-password email: when email is integrated.
- Minimum password length on tenant sign-up (`Register`): today it has none. Worth a separate fix;
  not changed here to keep this phase focused.
- Deleting users: deactivate only (keeps audit history and `ApplicationEvent` authorship intact).
- Last-Owner race: two Owners demoting each other at the same instant could both pass the check.
  Accepted; a serialisable transaction can be added if it is ever seen.
- Localised (NL/EN) strings: the codebase has no localisation yet; strings follow the existing inline
  English convention.

## Manual gate
Task 2 adds a migration. **After Task 2 the lead stops and asks the developer to apply it** before
any later task runs Playwright (EF will select the new columns on sign-in):
```
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```
After deployment every existing session is signed out once (old cookies carry no stamp claim).

## File map

| File | Change |
|---|---|
| `src/Ats.Domain/Authorization/AtsPermission.cs`, `RolePermissions.cs` | `ProfileManage` permission for every role |
| `src/Ats.Domain/Enums/AtsRole.cs` | `Assignable` |
| `src/Ats.Domain/Entities/AppUser.cs`, `Persistence/Configurations/AppUserConfiguration.cs` | new columns + defaults |
| `src/Ats.Infrastructure/Migrations/*_AddUserManagement.cs` | generated |
| `src/Ats.Application/Abstractions/IIdentityService.cs`, `Infrastructure/Identity/IdentityService.cs` | stamp in sign-in result, inactive check, `GetSessionAsync` |
| `src/Ats.Web/Identity/SessionValidator.cs`, `AtsSignIn.cs` | new |
| `src/Ats.Web/Program.cs`, `Controllers/AccountController.cs` | wire validator, shared sign-in |
| `src/Ats.Application/Users/*`, `src/Ats.Infrastructure/Persistence/Repositories/UserRepository.cs`, `DependencyInjection.cs` | new service + repository |
| `src/Ats.Web/Identity/RequirePasswordChangeFilter.cs`, `Controllers/ProfileController.cs`, `Views/Profile/ChangePassword.cshtml` | forced password change |
| `src/Ats.Web/Controllers/UsersController.cs`, `Views/Users/*`, `Models/UserViewModels.cs` | Users screen |
| `SidebarNavViewComponent.cs`, `TopBarViewComponent.cs`, sidebar view, `wwwroot/css/ats-components.css` | nav, crumbs, table grid |
| `tests/Ats.Tests/...` | role map, session, service rules, controller coverage |
| `tests/e2e/users.spec.ts` | end-to-end per role |
| Docs | authorization skill, multi-tenancy rule + skill, spec, CLAUDE.md |

---

### Task 1: `profile.manage` permission and assignable roles

**Files:**
- Modify: `src/Ats.Domain/Authorization/AtsPermission.cs`
- Modify: `src/Ats.Domain/Authorization/RolePermissions.cs`
- Modify: `src/Ats.Domain/Enums/AtsRole.cs`
- Test: `tests/Ats.Tests/Authorization/RolePermissionsTests.cs`

Every signed-in user needs to change their own password. The controller-coverage test requires a
named permission on every non-anonymous action, so this is an explicit permission every role holds.

- [ ] **Step 1: Update the tests first**

In `RolePermissionsTests.cs`, the shared `ReadOnly` set gains `AtsPermission.ProfileManage` (every
role's expected set then includes it), and add:
```csharp
[Fact]
public void Every_role_can_manage_its_own_profile() =>
    Assert.All(AtsRole.All, r => Assert.True(RolePermissions.Has(r, AtsPermission.ProfileManage), r));

[Fact]
public void HiringManager_is_not_assignable_until_it_is_scoped()
{
    Assert.DoesNotContain(AtsRole.HiringManager, AtsRole.Assignable);
    Assert.Equal(new[] { AtsRole.Owner, AtsRole.Recruiter, AtsRole.Viewer }, AtsRole.Assignable);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~Ats.Tests.Authorization`
Expected: build FAIL (`ProfileManage`, `Assignable` do not exist).

- [ ] **Step 3: Implement**

`AtsPermission.cs`: add `public const string ProfileManage = "profile.manage";` and add it to `All`.
`RolePermissions.cs`: add `AtsPermission.ProfileManage` to the shared `ReadOnly` baseline (Owner already
gets it through `All`).
`AtsRole.cs`:
```csharp
// Roles an Owner may give a user from the Users screen. HiringManager is withheld until phase 3
// limits it to its own jobs; until then it would see every job and candidate in the tenant.
public static readonly string[] Assignable = { Owner, Recruiter, Viewer };
```

- [ ] **Step 4: Run tests**

Run: `dotnet test Ats.slnx`. Expected: all green (the policy test in `PermissionPolicyTests` picks up
the new permission automatically).

- [ ] **Step 5: Hygiene**

Mutation: remove `ProfileManage` from `ReadOnly`, confirm `Every_role_can_manage_its_own_profile`
fails, restore. `dotnet build Ats.slnx` warning-clean, `dotnet format Ats.slnx --verify-no-changes`.

---

### Task 2: `AppUser` columns and migration

**Files:**
- Modify: `src/Ats.Domain/Entities/AppUser.cs`
- Modify: `src/Ats.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`
- Create (generated): `src/Ats.Infrastructure/Migrations/<timestamp>_AddUserManagement.cs` + Designer + snapshot update

- [ ] **Step 1: Entity**

`AppUser.cs`, after `Role`:
```csharp
public bool IsActive { get; set; } = true;
// Set for a temporary password (new user or admin reset); cleared when the user sets their own.
public bool MustChangePassword { get; set; }
// Rotated on every security-relevant change; a cookie carrying an older value is rejected.
public Guid SecurityStamp { get; set; } = Guid.NewGuid();
```

- [ ] **Step 2: Configuration**

`AppUserConfiguration.cs`, add:
```csharp
// Existing users stay active. Sentinel true: EF omits the value when it is true (the database default
// applies) and sends false explicitly, which avoids EF's "bool with a non-false default" warning.
b.Property(u => u.IsActive).HasDefaultValue(true).HasSentinel(true);
// Existing rows get a fresh stamp from the database; new rows get one from the entity initialiser.
b.Property(u => u.SecurityStamp).HasDefaultValueSql("NEWID()");
```
`MustChangePassword` needs no configuration: the generated `AddColumn` defaults it to `false` for
existing rows. The build must stay warning-clean; if EF still reports a default-value warning, report it
rather than suppressing it.

- [ ] **Step 3: Create the migration file (allowed; applying it is not)**

Run:
```
dotnet ef migrations add AddUserManagement --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```
Inspect the generated `Up`: three `AddColumn` calls on `Users` with `defaultValue: true`,
`defaultValue: false` and `defaultValueSql: "NEWID()"`, and nothing else. `Down` drops the three columns.

- [ ] **Step 4: Verify**

`dotnet build Ats.slnx` (no warnings), `dotnet test Ats.slnx`, `dotnet format Ats.slnx --verify-no-changes`.
Do NOT run `dotnet ef database update`. Report the migration file name.

- [ ] **Step 5: MANUAL GATE (lead)**

Lead prints the `database update` command from "Manual gate" above and waits for the developer to
confirm it has been applied before dispatching Task 3.

---

### Task 3: Security stamp in sign-in and per-request session validation

Read `.claude/skills/multitenancy/SKILL.md` first: this task extends a documented filter-bypass spot.

**Files:**
- Modify: `src/Ats.Application/Abstractions/IIdentityService.cs`
- Modify: `src/Ats.Infrastructure/Identity/IdentityService.cs`
- Create: `src/Ats.Web/Identity/AtsSignIn.cs`
- Create: `src/Ats.Web/Identity/SessionValidator.cs`
- Modify: `src/Ats.Web/Program.cs`, `src/Ats.Web/Controllers/AccountController.cs`
- Modify: `tests/Ats.Tests/Tenancy/TenantOnboardingServiceTests.cs` (its `FakeIdentity`)
- Test: `tests/Ats.Tests/Authorization/SessionValidatorTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/Ats.Tests/Authorization/SessionValidatorTests.cs`:
```csharp
using Ats.Application.Abstractions;
using Ats.Web.Identity;
using Xunit;

namespace Ats.Tests.Authorization;

// The per-request check that makes role changes, deactivation and password resets take effect at once.
public class SessionValidatorTests
{
    private static readonly Guid Stamp = Guid.NewGuid();

    [Fact]
    public void Active_user_with_matching_stamp_is_current() =>
        Assert.True(SessionValidator.IsCurrent(new UserSession(true, Stamp, "Viewer", false), Stamp.ToString()));

    [Fact]
    public void Rotated_stamp_is_rejected() =>
        Assert.False(SessionValidator.IsCurrent(new UserSession(true, Guid.NewGuid(), "Viewer", false), Stamp.ToString()));

    [Fact]
    public void Inactive_user_is_rejected() =>
        Assert.False(SessionValidator.IsCurrent(new UserSession(false, Stamp, "Viewer", false), Stamp.ToString()));

    [Fact]
    public void Missing_user_is_rejected() =>
        Assert.False(SessionValidator.IsCurrent(null, Stamp.ToString()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void Cookie_without_a_valid_stamp_is_rejected(string? claim) =>
        Assert.False(SessionValidator.IsCurrent(new UserSession(true, Stamp, "Viewer", false), claim));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~SessionValidatorTests`
Expected: build FAIL (`UserSession`, `SessionValidator` do not exist).

- [ ] **Step 3: Abstractions**

`IIdentityService.cs`:
```csharp
namespace Ats.Application.Abstractions;

public record SignInResult(bool Succeeded, int? UserId, int? TenantId, string? Role, string? DisplayName, string? Error,
    Guid? SecurityStamp = null, bool MustChangePassword = false);

// What the cookie is checked against on each request. IsActive is false when the user is deactivated
// or the tenant is suspended.
public sealed record UserSession(bool IsActive, Guid SecurityStamp, string Role, bool MustChangePassword);

public interface IIdentityService
{
    Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default);
    Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);
    Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default);
    string HashPassword(string password);
    bool VerifyPassword(string hash, string password);
}
```
Update the `FakeIdentity` in `TenantOnboardingServiceTests.cs` with
`public Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default) => Task.FromResult<UserSession?>(null);`.

- [ ] **Step 4: IdentityService**

In `ValidateCredentialsAsync`, after the password check and before the tenant check:
```csharp
if (!user.IsActive)
    return new SignInResult(false, null, null, null, null, "This account is not available. Contact your administrator.");
```
The success return becomes:
```csharp
return new SignInResult(true, user.Id, user.TenantId, user.Role, user.DisplayName, null,
    user.SecurityStamp, user.MustChangePassword);
```
Add:
```csharp
public async Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default)
{
    // IgnoreQueryFilters: this runs while the cookie is being validated, before HttpContext.User (and so
    // the tenant context) exists. It is scoped explicitly by the tenant_id from the same cookie.
    return await _db.Users.IgnoreQueryFilters()
        .Where(u => u.Id == userId && u.TenantId == tenantId)
        .Select(u => new UserSession(
            u.IsActive && _db.Tenants.Any(t => t.Id == u.TenantId && t.Status == TenantStatus.Active),
            u.SecurityStamp, u.Role, u.MustChangePassword))
        .SingleOrDefaultAsync(ct);
}
```
If EF cannot translate the positional record constructor in `Select`, project to an anonymous type and
build the record after the query; keep it one round trip.

- [ ] **Step 5: Shared sign-in helper**

`src/Ats.Web/Identity/AtsSignIn.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Ats.Web.Identity;

public static class AtsSignIn
{
    public const string Scheme = "AtsCookie";
    public const string TenantClaim = "tenant_id";
    public const string StampClaim = "security_stamp";
    public const string MustChangePasswordClaim = "must_change_password";

    public static Task SignInAsync(HttpContext http, int userId, int tenantId, string role, string displayName,
        Guid securityStamp, bool mustChangePassword)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, role),
            new(TenantClaim, tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(StampClaim, securityStamp.ToString()),
        };
        if (mustChangePassword) claims.Add(new(MustChangePasswordClaim, "true"));
        return http.SignInAsync(Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)));
    }
}
```
(Match the existing `tenant_id` claim name; `HttpTenantContext` reads it. Do not rename it.)

`AccountController.cs`:
- `Login`: replace the private `SignInAsync` call with
  `await AtsSignIn.SignInAsync(HttpContext, result.UserId!.Value, result.TenantId!.Value, result.Role!, result.DisplayName ?? "", result.SecurityStamp!.Value, result.MustChangePassword);`
- `Register`: after a successful `RegisterAsync`, sign in through
  `var signIn = await _identity.ValidateCredentialsAsync(vm.OwnerEmail, vm.Password);` and the same
  helper, so the new Owner's cookie carries their stamp. If that unexpectedly fails, redirect to `Login`.
- Delete the private `SignInAsync` method.

- [ ] **Step 6: Session validator**

`src/Ats.Web/Identity/SessionValidator.cs`:
```csharp
using System.Security.Claims;
using Ats.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Ats.Web.Identity;

// Checks the cookie against the database on every non-anonymous request (one indexed lookup; there is
// no server-side cache by design). A role change, deactivation, password reset or tenant suspension
// therefore signs the user out on their next request.
public static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        // Static assets, health checks, sign-in and the career site do not use the user; skip the lookup.
        if (ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null) return;

        var principal = ctx.Principal;
        if (principal is null
            || !int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            || !int.TryParse(principal.FindFirst(AtsSignIn.TenantClaim)?.Value, out var tenantId))
        {
            await RejectAsync(ctx);
            return;
        }

        var identity = ctx.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
        var session = await identity.GetSessionAsync(userId, tenantId, ctx.HttpContext.RequestAborted);
        if (!IsCurrent(session, principal.FindFirst(AtsSignIn.StampClaim)?.Value))
            await RejectAsync(ctx);
    }

    public static bool IsCurrent(UserSession? session, string? stampClaim) =>
        session is { IsActive: true }
        && Guid.TryParse(stampClaim, out var stamp)
        && stamp == session.SecurityStamp;

    private static async Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(AtsSignIn.Scheme);
    }
}
```

`Program.cs`, inside the cookie options (keep `OnRedirectToAccessDenied`):
```csharp
o.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
```
Replace the `"AtsCookie"` literals in `Program.cs` with `AtsSignIn.Scheme`.

- [ ] **Step 7: Verify**

`dotnet test Ats.slnx` (SessionValidatorTests 7 cases green), build warning-clean, format clean.
Mutation: make `IsCurrent` ignore the stamp comparison, confirm `Rotated_stamp_is_rejected` fails, restore.
Then `npx playwright test` (requires the Task 2 migration applied). The first run signs in fresh, so
`auth.setup.ts` must pass. Report evidence.

---

### Task 4: `UserService` and repository (the business rules)

**Files:**
- Create: `src/Ats.Application/Users/IUserRepository.cs`
- Create: `src/Ats.Application/Users/UserService.cs`
- Create: `src/Ats.Infrastructure/Persistence/Repositories/UserRepository.cs`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`
- Create: `tests/Ats.Tests/Fakes/FakeUserRepository.cs`
- Test: `tests/Ats.Tests/Users/UserServiceTests.cs`

- [ ] **Step 1: Abstractions**

`src/Ats.Application/Users/IUserRepository.cs`:
```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Users;

// Projection for the Users screen: never carries the password hash.
public sealed record UserListItem(int Id, string DisplayName, string Email, string Role, bool IsActive, DateTimeOffset CreatedAt);

public interface IUserRepository
{
    Task<List<UserListItem>> ListAsync(CancellationToken ct = default);
    Task<AppUser?> GetAsync(int id, CancellationToken ct = default);
    Task<int> CountActiveOwnersAsync(CancellationToken ct = default);
    Task AddAsync(AppUser user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Fake and failing tests**

`tests/Ats.Tests/Fakes/FakeUserRepository.cs`:
```csharp
using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Tests.Fakes;

public sealed class FakeUserRepository : IUserRepository
{
    public List<AppUser> Users { get; } = new();
    public int SaveCount { get; private set; }

    public Task<List<UserListItem>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(Users.Select(u => new UserListItem(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.CreatedAt)).ToList());

    public Task<AppUser?> GetAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<int> CountActiveOwnersAsync(CancellationToken ct = default) =>
        Task.FromResult(Users.Count(u => u.IsActive && u.Role == AtsRole.Owner));

    public Task AddAsync(AppUser user, CancellationToken ct = default)
    {
        user.Id = Users.Count == 0 ? 1 : Users.Max(u => u.Id) + 1;
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
```

`tests/Ats.Tests/Users/UserServiceTests.cs`:
```csharp
using Ats.Application.Abstractions;
using Ats.Application.Tenancy;
using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Users;

// These rules decide who can act in a tenant: a wrong one locks a customer out (no Owner left) or
// lets a user keep access after it was taken away (stamp not rotated).
public class UserServiceTests
{
    private const string Temp = "temporary-pass-1";
    private const int OwnerId = 1;

    private static (UserService Service, FakeUserRepository Repo, FakeEmails Emails) Build()
    {
        var repo = new FakeUserRepository();
        repo.Users.Add(new AppUser { Id = OwnerId, Email = "owner@acme.test", DisplayName = "Owner", Role = AtsRole.Owner, PasswordHash = "hash:owner-password-1" });
        repo.Users.Add(new AppUser { Id = 2, Email = "rec@acme.test", DisplayName = "Rec", Role = AtsRole.Recruiter, PasswordHash = "hash:x" });
        var emails = new FakeEmails();
        return (new UserService(repo, emails, new FakeHasher()), repo, emails);
    }

    private static CreateUserInput Input(string role = AtsRole.Viewer, string email = " New.User@Acme.test ", string password = Temp) =>
        new("New User", email, role, password);

    // Create

    [Fact]
    public async Task Create_adds_an_active_user_who_must_change_password()
    {
        var (s, repo, emails) = Build();
        var result = await s.CreateAsync(Input());
        Assert.True(result.Succeeded, result.Error);
        var u = repo.Users.Single(x => x.Email == "new.user@acme.test");
        Assert.True(u.IsActive);
        Assert.True(u.MustChangePassword);
        Assert.Equal(AtsRole.Viewer, u.Role);
        Assert.Equal("hash:" + Temp, u.PasswordHash);
        Assert.Equal("new.user@acme.test", emails.Checked);
    }

    [Fact]
    public async Task Create_rejects_HiringManager()
    {
        var (s, repo, _) = Build();
        var result = await s.CreateAsync(Input(AtsRole.HiringManager));
        Assert.False(result.Succeeded);
        Assert.Equal(2, repo.Users.Count);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("owner")]
    [InlineData("")]
    public async Task Create_rejects_unknown_roles(string role) =>
        Assert.False((await Build().Service.CreateAsync(Input(role))).Succeeded);

    [Fact]
    public async Task Create_rejects_an_email_registered_in_any_tenant()
    {
        var (s, repo, emails) = Build();
        emails.Taken = true;
        var result = await s.CreateAsync(Input());
        Assert.Equal("That email address is already registered.", result.Error);
        Assert.Equal(0, repo.SaveCount);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Create_rejects_invalid_email(string email) =>
        Assert.False((await Build().Service.CreateAsync(Input(email: email))).Succeeded);

    [Theory]
    [InlineData("short-pw-11")]   // 11 characters
    [InlineData("")]
    public async Task Create_rejects_short_passwords(string password) =>
        Assert.False((await Build().Service.CreateAsync(Input(password: password))).Succeeded);

    [Fact]
    public async Task Create_rejects_a_blank_name() =>
        Assert.False((await Build().Service.CreateAsync(new CreateUserInput("  ", "a@acme.test", AtsRole.Viewer, Temp))).Succeeded);

    // Role changes

    [Fact]
    public async Task Changing_a_role_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        var result = await s.ChangeRoleAsync(2, AtsRole.Viewer, OwnerId);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(AtsRole.Viewer, repo.Users[1].Role);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
    }

    [Fact]
    public async Task An_Owner_cannot_change_their_own_role()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2" });
        var result = await s.ChangeRoleAsync(OwnerId, AtsRole.Viewer, OwnerId);
        Assert.False(result.Succeeded);
        Assert.Equal(AtsRole.Owner, repo.Users[0].Role);
    }

    [Fact]
    public async Task The_last_active_Owner_cannot_be_demoted()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", IsActive = false });
        var result = await s.ChangeRoleAsync(OwnerId, AtsRole.Viewer, actingUserId: 3);
        Assert.Equal("A workspace needs at least one active Owner.", result.Error);
    }

    [Fact]
    public async Task An_Owner_can_be_demoted_when_another_active_Owner_exists()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2" });
        Assert.True((await s.ChangeRoleAsync(3, AtsRole.Recruiter, OwnerId)).Succeeded);
    }

    [Fact]
    public async Task Role_cannot_be_changed_to_HiringManager() =>
        Assert.False((await Build().Service.ChangeRoleAsync(2, AtsRole.HiringManager, OwnerId)).Succeeded);

    [Fact]
    public async Task Changing_the_role_of_an_unknown_user_fails() =>
        Assert.Equal("User not found.", (await Build().Service.ChangeRoleAsync(99, AtsRole.Viewer, OwnerId)).Error);

    // Activation

    [Fact]
    public async Task Deactivating_rotates_the_stamp_and_blocks_the_user()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        Assert.True((await s.SetActiveAsync(2, false, OwnerId)).Succeeded);
        Assert.False(repo.Users[1].IsActive);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
    }

    [Fact]
    public async Task A_user_cannot_deactivate_themselves() =>
        Assert.False((await Build().Service.SetActiveAsync(OwnerId, false, OwnerId)).Succeeded);

    [Fact]
    public async Task The_last_active_Owner_cannot_be_deactivated()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Recruiter, Email = "r2@acme.test", DisplayName = "R2" });
        Assert.False((await s.SetActiveAsync(OwnerId, false, actingUserId: 3)).Succeeded);
        Assert.True(repo.Users[0].IsActive);
    }

    [Fact]
    public async Task Reactivating_restores_access()
    {
        var (s, repo, _) = Build();
        repo.Users[1].IsActive = false;
        Assert.True((await s.SetActiveAsync(2, true, OwnerId)).Succeeded);
        Assert.True(repo.Users[1].IsActive);
    }

    // Passwords

    [Fact]
    public async Task Admin_reset_sets_a_temporary_password_and_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        Assert.True((await s.ResetPasswordAsync(2, Temp, OwnerId)).Succeeded);
        Assert.Equal("hash:" + Temp, repo.Users[1].PasswordHash);
        Assert.True(repo.Users[1].MustChangePassword);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
    }

    [Fact]
    public async Task Admin_reset_of_own_account_is_refused() =>
        Assert.False((await Build().Service.ResetPasswordAsync(OwnerId, Temp, OwnerId)).Succeeded);

    [Fact]
    public async Task Changing_own_password_clears_the_flag_and_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        repo.Users[0].MustChangePassword = true;
        var before = repo.Users[0].SecurityStamp;
        var result = await s.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "a-brand-new-pass");
        Assert.True(result.Succeeded, result.Error);
        Assert.False(repo.Users[0].MustChangePassword);
        Assert.Equal("hash:a-brand-new-pass", repo.Users[0].PasswordHash);
        Assert.NotEqual(before, repo.Users[0].SecurityStamp);
    }

    [Fact]
    public async Task Changing_own_password_needs_the_current_one() =>
        Assert.Equal("Current password is incorrect.",
            (await Build().Service.ChangeOwnPasswordAsync(OwnerId, "wrong", "a-brand-new-pass")).Error);

    [Fact]
    public async Task New_password_must_differ_from_the_current_one() =>
        Assert.False((await Build().Service.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "owner-password-1")).Succeeded);

    [Fact]
    public async Task New_password_must_be_long_enough() =>
        Assert.False((await Build().Service.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "short")).Succeeded);

    private sealed class FakeEmails : IOnboardingStore
    {
        public bool Taken { get; set; }
        public string? Checked { get; private set; }
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct) { Checked = email; return Task.FromResult(Taken); }
        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
        public Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(Tenant tenant, TenantSettings settings,
            PipelineTemplate template, string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHasher : IIdentityService
    {
        public string HashPassword(string password) => "hash:" + password;
        public bool VerifyPassword(string hash, string password) => hash == "hash:" + password;
        public Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test Ats.slnx --filter FullyQualifiedName~UserServiceTests`
Expected: build FAIL (`UserService`, `CreateUserInput` missing).

- [ ] **Step 4: Implement the service**

`src/Ats.Application/Users/UserService.cs`:
```csharp
using System.Net.Mail;
using Ats.Application.Abstractions;
using Ats.Application.Common;
using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Users;

public sealed record CreateUserInput(string DisplayName, string Email, string Role, string TemporaryPassword);

public interface IUserService
{
    Task<List<UserListItem>> ListAsync(CancellationToken ct = default);
    Task<UserListItem?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(CreateUserInput input, CancellationToken ct = default);
    Task<OperationResult> ChangeRoleAsync(int userId, string role, int actingUserId, CancellationToken ct = default);
    Task<OperationResult> SetActiveAsync(int userId, bool active, int actingUserId, CancellationToken ct = default);
    Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword, int actingUserId, CancellationToken ct = default);
    Task<OperationResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default);
}

public sealed class UserService : IUserService
{
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;
    private const string LastOwner = "A workspace needs at least one active Owner.";
    private const string NotFound = "User not found.";

    private readonly IUserRepository _repo;
    private readonly IOnboardingStore _emails;   // global email check; email is unique across tenants
    private readonly IIdentityService _identity;

    public UserService(IUserRepository repo, IOnboardingStore emails, IIdentityService identity)
    {
        _repo = repo; _emails = emails; _identity = identity;
    }

    public Task<List<UserListItem>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);

    public async Task<UserListItem?> GetAsync(int id, CancellationToken ct = default)
    {
        var u = await _repo.GetAsync(id, ct);
        return u is null ? null : new UserListItem(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.CreatedAt);
    }

    public async Task<OperationResult> CreateAsync(CreateUserInput input, CancellationToken ct = default)
    {
        var name = input.DisplayName?.Trim() ?? "";
        if (name.Length == 0) return OperationResult.Fail("Name is required.");
        if (name.Length > 200) return OperationResult.Fail("Name must be 200 characters or fewer.");

        var email = input.Email?.Trim().ToLowerInvariant() ?? "";
        if (!IsEmail(email)) return OperationResult.Fail("Enter a valid email address.");
        if (!AtsRole.Assignable.Contains(input.Role)) return OperationResult.Fail("Choose a valid role.");
        if (PasswordError(input.TemporaryPassword) is { } pwError) return OperationResult.Fail(pwError);
        if (await _emails.EmailExistsAsync(email, ct)) return OperationResult.Fail("That email address is already registered.");

        await _repo.AddAsync(new AppUser
        {
            DisplayName = name,
            Email = email,
            Role = input.Role,
            PasswordHash = _identity.HashPassword(input.TemporaryPassword),
            MustChangePassword = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ChangeRoleAsync(int userId, string role, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return OperationResult.Fail("You cannot change your own role.");
        if (!AtsRole.Assignable.Contains(role)) return OperationResult.Fail("Choose a valid role.");
        var user = await _repo.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail(NotFound);
        if (user.Role == role) return OperationResult.Ok;
        if (await IsLastActiveOwnerAsync(user, ct)) return OperationResult.Fail(LastOwner);

        user.Role = role;
        user.SecurityStamp = Guid.NewGuid();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> SetActiveAsync(int userId, bool active, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return OperationResult.Fail("You cannot deactivate your own account.");
        var user = await _repo.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail(NotFound);
        if (user.IsActive == active) return OperationResult.Ok;
        if (!active && await IsLastActiveOwnerAsync(user, ct)) return OperationResult.Fail(LastOwner);

        user.IsActive = active;
        user.SecurityStamp = Guid.NewGuid();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword, int actingUserId, CancellationToken ct = default)
    {
        if (userId == actingUserId) return OperationResult.Fail("Use Change password for your own account.");
        if (PasswordError(temporaryPassword) is { } pwError) return OperationResult.Fail(pwError);
        var user = await _repo.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail(NotFound);

        user.PasswordHash = _identity.HashPassword(temporaryPassword);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _repo.GetAsync(userId, ct);
        if (user is null) return OperationResult.Fail(NotFound);
        if (!_identity.VerifyPassword(user.PasswordHash, currentPassword ?? ""))
            return OperationResult.Fail("Current password is incorrect.");
        if (PasswordError(newPassword) is { } pwError) return OperationResult.Fail(pwError);
        if (newPassword == currentPassword) return OperationResult.Fail("Choose a password different from the current one.");

        user.PasswordHash = _identity.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();   // signs out any other session; the caller re-issues this one
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    private async Task<bool> IsLastActiveOwnerAsync(AppUser user, CancellationToken ct) =>
        user.Role == AtsRole.Owner && user.IsActive && await _repo.CountActiveOwnersAsync(ct) <= 1;

    private static string? PasswordError(string? password) =>
        password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength
            ? $"Password must be {MinPasswordLength} to {MaxPasswordLength} characters."
            : null;

    private static bool IsEmail(string email) =>
        email.Length is > 0 and <= 256 && MailAddress.TryCreate(email, out var a) && a.Address == email;
}
```

- [ ] **Step 5: Repository and DI**

`src/Ats.Infrastructure/Persistence/Repositories/UserRepository.cs` (tenant-filtered; no bypass, no
hand-set `TenantId`: the interceptor stamps inserts):
```csharp
using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AtsDbContext _db;
    public UserRepository(AtsDbContext db) => _db = db;

    public Task<List<UserListItem>> ListAsync(CancellationToken ct = default) =>
        _db.Users.AsNoTracking()
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.DisplayName)
            .Select(u => new UserListItem(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);

    public Task<AppUser?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<int> CountActiveOwnersAsync(CancellationToken ct = default) =>
        _db.Users.CountAsync(u => u.IsActive && u.Role == AtsRole.Owner, ct);

    public async Task AddAsync(AppUser user, CancellationToken ct = default) => await _db.Users.AddAsync(user, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```
Match the style of the existing repositories if they differ (e.g. `_db.Set<AppUser>()`); keep the
projection so the hash never leaves the repository in the list.

`DependencyInjection.cs`, beside the Department registrations:
```csharp
services.AddScoped<IUserRepository, UserRepository>();
services.AddScoped<IUserService, UserService>();
```

- [ ] **Step 6: Verify**

`dotnet test Ats.slnx` green. Mutation checks (restore each): remove the `IsLastActiveOwnerAsync` guard
in `SetActiveAsync` (expect `The_last_active_Owner_cannot_be_deactivated` to fail); remove the stamp
rotation in `ChangeRoleAsync` (expect `Changing_a_role_rotates_the_stamp` to fail). Build and format clean.

---

### Task 5: Change password page and forced change

**Files:**
- Create: `src/Ats.Web/Controllers/ProfileController.cs`
- Create: `src/Ats.Web/Models/ChangePasswordViewModel.cs`
- Create: `src/Ats.Web/Views/Profile/ChangePassword.cshtml`
- Create: `src/Ats.Web/Identity/RequirePasswordChangeFilter.cs`
- Modify: `src/Ats.Web/Program.cs` (register the filter)
- Modify: sidebar user block view (`Views/Shared/Components/SidebarNav/Default.cshtml`): add a "Change password" link
- Modify: `src/Ats.Web/ViewComponents/TopBarViewComponent.cs`: crumb `["Profile"] = ("Account", "Change password")`
- Test: `tests/Ats.Tests/Authorization/ControllerAuthorizationTests.cs` (spot check)

- [ ] **Step 1: Test first**

Add to `Action_requires_policy`:
```csharp
[InlineData(typeof(ProfileController), "ChangePassword", true, AtsPermission.ProfileManage)]
```
Run: build FAIL (`ProfileController` missing).

- [ ] **Step 2: View model**

```csharp
using System.ComponentModel.DataAnnotations;
using Ats.Application.Users;

namespace Ats.Web.Models;

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "New password")]
    [StringLength(UserService.MaxPasswordLength, MinimumLength = UserService.MinPasswordLength)]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}
```

- [ ] **Step 3: Controller**

```csharp
using Ats.Application.Abstractions;
using Ats.Application.Auditing;
using Ats.Application.Users;
using Ats.Domain.Authorization;
using Ats.Web.Identity;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Policy = AtsPermission.ProfileManage)]
public class ProfileController : Controller
{
    private readonly IUserService _users;
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _current;
    private readonly ITenantContext _tenant;
    private readonly IAuditLogger _audit;

    public ProfileController(IUserService users, IIdentityService identity, ICurrentUser current, ITenantContext tenant, IAuditLogger audit)
    {
        _users = users; _identity = identity; _current = current; _tenant = tenant; _audit = audit;
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = _current.UserId!.Value;
        var result = await _users.ChangeOwnPasswordAsync(userId, vm.CurrentPassword, vm.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(vm);
        }

        // The stamp just rotated, so this cookie is now stale: re-issue it without the must-change flag.
        var tenantId = _tenant.CurrentTenantId!.Value;
        var session = await _identity.GetSessionAsync(userId, tenantId);
        if (session is null) return RedirectToAction("Login", "Account");
        await AtsSignIn.SignInAsync(HttpContext, userId, tenantId, session.Role, _current.Name ?? "", session.SecurityStamp, false);

        await _audit.LogAsync("PasswordChanged", "User", userId.ToString(System.Globalization.CultureInfo.InvariantCulture), "Changed own password");
        TempData["Success"] = "Password changed.";
        return RedirectToAction("Index", "Dashboard");
    }
}
```

- [ ] **Step 4: View**

`Views/Profile/ChangePassword.cshtml` (back-office layout; follow the UI skill's form pattern, as in
`Candidates/Form.cshtml`):
```cshtml
@model Ats.Web.Models.ChangePasswordViewModel
@{
    ViewData["Title"] = "Change password";
    var forced = User.FindFirst(Ats.Web.Identity.AtsSignIn.MustChangePasswordClaim) is not null;
}
@if (forced)
{
    <div class="alert alert-info d-flex align-items-center gap-2" role="status">
        <span class="ms ms-sm">lock_reset</span> You signed in with a temporary password. Set your own password to continue.
    </div>
}
<form asp-action="ChangePassword" method="post" class="col-md-6">
    <div asp-validation-summary="ModelOnly" role="alert" class="text-danger small mb-2"></div>
    <div class="mb-3"><label asp-for="CurrentPassword" class="form-label"></label>
        <input asp-for="CurrentPassword" class="form-control" autocomplete="current-password" />
        <span asp-validation-for="CurrentPassword" class="text-danger small"></span></div>
    <div class="mb-3"><label asp-for="NewPassword" class="form-label"></label>
        <input asp-for="NewPassword" class="form-control" autocomplete="new-password" aria-describedby="pw-help" />
        <div id="pw-help" class="form-text">At least 12 characters.</div>
        <span asp-validation-for="NewPassword" class="text-danger small"></span></div>
    <div class="mb-3"><label asp-for="ConfirmPassword" class="form-label"></label>
        <input asp-for="ConfirmPassword" class="form-control" autocomplete="new-password" />
        <span asp-validation-for="ConfirmPassword" class="text-danger small"></span></div>
    <div class="d-flex align-items-center gap-2 border-top pt-3">
        <button type="submit" class="btn btn-primary">Change password</button>
        @if (!forced) { <a class="btn btn-outline-secondary" asp-controller="Dashboard" asp-action="Index">Cancel</a> }
    </div>
</form>
```

- [ ] **Step 5: Forced-change filter**

`src/Ats.Web/Identity/RequirePasswordChangeFilter.cs`:
```csharp
using Ats.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ats.Web.Identity;

// Holds a user who signed in with a temporary password on the change-password page. Sign-out, error
// pages and the public career site stay reachable.
public sealed class RequirePasswordChangeFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.User.FindFirst(AtsSignIn.MustChangePasswordClaim) is null) return;
        if (context.Controller is ProfileController or AccountController or HomeController) return;
        if (context.RouteData.Values.ContainsKey("area")) return;
        context.Result = new RedirectToActionResult(nameof(ProfileController.ChangePassword), "Profile", null);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
```
`Program.cs`, in `AddControllersWithViews`:
```csharp
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<RequirePasswordChangeFilter>();
});
```
Check `RouteData.Values.ContainsKey("area")` against how the Careers area routes (it may carry an empty
`area` value on non-area routes; if so, test for a non-empty value instead).

- [ ] **Step 6: Sidebar link and crumb**

In the sidebar user block (where the name and role render, near sign-out), add a
`<a asp-controller="Profile" asp-action="ChangePassword">Change password</a>` styled like the adjacent
sign-out control (UI skill; no inline hex, Material Symbols icon `key`). Add the TopBar crumb.

- [ ] **Step 7: Verify**

`dotnet test Ats.slnx`, build, format. `npx playwright test` (Owner unaffected: the flag is false for
existing users). Manual check is covered in Task 7's e2e.

---

### Task 6: Users screen

Read `.claude/skills/ui/SKILL.md` and follow `Views/Organisation/Index.cshtml` and `Views/Jobs/Index.cshtml`
for table, row menu, confirm and flash patterns.

**Files:**
- Create: `src/Ats.Web/Controllers/UsersController.cs`
- Create: `src/Ats.Web/Models/UserViewModels.cs`
- Create: `src/Ats.Web/Views/Users/Index.cshtml`, `Create.cshtml`, `Edit.cshtml`, `ResetPassword.cshtml`
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`, `TopBarViewComponent.cs`
- Modify: `src/Ats.Web/wwwroot/css/ats-components.css`
- Test: `tests/Ats.Tests/Authorization/ControllerAuthorizationTests.cs`

- [ ] **Step 1: Tests first**

Add to `Action_requires_policy`:
```csharp
[InlineData(typeof(UsersController), "Index", false, AtsPermission.UsersManage)]
[InlineData(typeof(UsersController), "Create", true, AtsPermission.UsersManage)]
[InlineData(typeof(UsersController), "Deactivate", true, AtsPermission.UsersManage)]
[InlineData(typeof(UsersController), "ResetPassword", true, AtsPermission.UsersManage)]
```
Run: build FAIL (`UsersController` missing).

- [ ] **Step 2: View models**

`src/Ats.Web/Models/UserViewModels.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using Ats.Application.Users;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public sealed record UsersIndexViewModel(IReadOnlyList<UserListItem> Users, int CurrentUserId);

public static class RoleOptions
{
    public static IEnumerable<SelectListItem> For(string? selected) =>
        AtsRole.Assignable.Select(r => new SelectListItem(r, r, r == selected));
}

public class UserCreateViewModel
{
    [Required, StringLength(200), Display(Name = "Name")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = AtsRole.Viewer;
    [Required, DataType(DataType.Password), Display(Name = "Temporary password")]
    [StringLength(UserService.MaxPasswordLength, MinimumLength = UserService.MinPasswordLength)]
    public string TemporaryPassword { get; set; } = "";
}

public class UserRoleViewModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = "";
}

public class UserResetPasswordViewModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    [Required, DataType(DataType.Password), Display(Name = "Temporary password")]
    [StringLength(UserService.MaxPasswordLength, MinimumLength = UserService.MinPasswordLength)]
    public string TemporaryPassword { get; set; } = "";
}
```

- [ ] **Step 3: Controller**

```csharp
using System.Globalization;
using Ats.Application.Abstractions;
using Ats.Application.Auditing;
using Ats.Application.Users;
using Ats.Domain.Authorization;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize(Policy = AtsPermission.UsersManage)]
public class UsersController : Controller
{
    private readonly IUserService _users;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public UsersController(IUserService users, ICurrentUser current, IAuditLogger audit)
    {
        _users = users; _current = current; _audit = audit;
    }

    private int Me => _current.UserId!.Value;

    public async Task<IActionResult> Index() =>
        View(new UsersIndexViewModel(await _users.ListAsync(), Me));

    [HttpGet] public IActionResult Create() => View(new UserCreateViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(UserCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _users.CreateAsync(new CreateUserInput(vm.DisplayName, vm.Email, vm.Role, vm.TemporaryPassword));
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(vm); }
        await _audit.LogAsync("UserCreated", "User", null, $"Added '{vm.Email.Trim().ToLowerInvariant()}' as {vm.Role}");
        TempData["Success"] = "User added. Share the temporary password securely; they must change it when they first sign in.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (id == Me) return RedirectToAction(nameof(Index));
        var u = await _users.GetAsync(id);
        if (u is null) return NotFound();
        return View(new UserRoleViewModel { Id = u.Id, DisplayName = u.DisplayName, Email = u.Email, Role = u.Role });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserRoleViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _users.ChangeRoleAsync(vm.Id, vm.Role, Me);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(vm); }
        await _audit.LogAsync("UserRoleChanged", "User", Ref(vm.Id), $"Changed role of '{vm.Email}' to {vm.Role}");
        TempData["Success"] = "Role updated. The change applies on their next click.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(int id)
    {
        if (id == Me) return RedirectToAction("ChangePassword", "Profile");
        var u = await _users.GetAsync(id);
        if (u is null) return NotFound();
        return View(new UserResetPasswordViewModel { Id = u.Id, DisplayName = u.DisplayName });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(UserResetPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _users.ResetPasswordAsync(vm.Id, vm.TemporaryPassword, Me);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.Error!); return View(vm); }
        await _audit.LogAsync("UserPasswordReset", "User", Ref(vm.Id), $"Reset password for '{vm.DisplayName}'");
        TempData["Success"] = "Password reset. They are signed out and must set a new password at next sign-in.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public Task<IActionResult> Deactivate(int id) => SetActive(id, false, "UserDeactivated", "User deactivated and signed out.");

    [HttpPost]
    public Task<IActionResult> Reactivate(int id) => SetActive(id, true, "UserReactivated", "User reactivated.");

    private async Task<IActionResult> SetActive(int id, bool active, string action, string success)
    {
        var result = await _users.SetActiveAsync(id, active, Me);
        if (result.Succeeded)
        {
            await _audit.LogAsync(action, "User", Ref(id), $"{(active ? "Reactivated" : "Deactivated")} user {id}");
            TempData["Success"] = success;
        }
        else TempData["Error"] = result.Error;   // match the flash key the layout already renders
        return RedirectToAction(nameof(Index));
    }

    private static string Ref(int id) => id.ToString(CultureInfo.InvariantCulture);
}
```
Check the flash key the layout renders for errors (see how `DepartmentsController.Delete` reports a
failure) and use the same key. Audit summaries must never contain a password.

- [ ] **Step 4: Views**

`Views/Users/Index.cshtml`:
```cshtml
@model Ats.Web.Models.UsersIndexViewModel
@using Ats.Web.Models.Shared
@{
    ViewData["Title"] = "Users";
    ViewData["Eyebrow"] = $"{Model.Users.Count(u => u.IsActive)} active:";
}

@section PageActions {
    <a class="btn btn-primary" asp-action="Create"><span class="ms ms-sm">person_add</span> Add user</a>
}

<div class="ats-card-flush">
    <div class="ats-thead ats-table--users">
        <span>User</span><span>Role</span><span>Status</span><span>Added</span><span></span>
    </div>
    @foreach (var u in Model.Users)
    {
        var isMe = u.Id == Model.CurrentUserId;
        <div class="ats-trow ats-table--users">
            <span class="d-flex align-items-center gap-2" style="min-width:0">
                <partial name="Partials/_Avatar" model="new AvatarModel(u.DisplayName, 2.125)" />
                <span class="ats-cell-stack">
                    <span class="ats-cell-title">@u.DisplayName</span>
                    <span class="ats-cell-sub">@u.Email</span>
                </span>
            </span>
            <span class="ats-small">@u.Role</span>
            <span><partial name="Partials/_StatusPill" model="new StatusPillModel(u.IsActive ? "Active" : "Deactivated", u.IsActive ? PillTone.Success : PillTone.Neutral)" /></span>
            <span class="ats-small ats-muted"><local-time utc="@u.CreatedAt" format="monthday"></local-time></span>
            <span class="d-flex justify-content-end ats-row-actions">
                @if (isMe)
                {
                    <span class="ats-chip ats-chip--neutral">You</span>
                }
                else
                {
                    <div class="dropdown">
                        <button type="button" class="btn btn-sm btn-outline-secondary border-0 px-2" data-bs-toggle="dropdown" data-bs-popper-config='{"strategy":"fixed"}' aria-label="Actions for @u.DisplayName">
                            <span class="ms ms-sm">more_horiz</span>
                        </button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            @if (u.IsActive)
                            {
                                <li><a class="dropdown-item" asp-action="Edit" asp-route-id="@u.Id">Change role</a></li>
                                <li><a class="dropdown-item" asp-action="ResetPassword" asp-route-id="@u.Id">Reset password</a></li>
                                <li><hr class="dropdown-divider"></li>
                                <li>
                                    <form asp-action="Deactivate" asp-route-id="@u.Id" method="post"
                                          hx-confirm="Deactivate @u.DisplayName? They are signed out at once and cannot sign in until reactivated."
                                          data-confirm-title="Deactivate user" data-confirm-ok="Deactivate" data-confirm-variant="danger">
                                        <button class="dropdown-item text-danger" type="submit">Deactivate</button>
                                    </form>
                                </li>
                            }
                            else
                            {
                                <li>
                                    <form asp-action="Reactivate" asp-route-id="@u.Id" method="post"
                                          hx-confirm="Reactivate @u.DisplayName?" data-confirm-title="Reactivate user" data-confirm-ok="Reactivate">
                                        <button class="dropdown-item" type="submit">Reactivate</button>
                                    </form>
                                </li>
                            }
                        </ul>
                    </div>
                }
            </span>
        </div>
    }
</div>
```
`Create.cshtml`: form like `Candidates/Form.cshtml`, fields `DisplayName`, `Email` (`autocomplete="off"`),
`Role` as `<select asp-for="Role" asp-items="RoleOptions.For(Model.Role)" class="form-select">`,
`TemporaryPassword` (`autocomplete="new-password"`, help text "At least 12 characters. Share it
securely; the user must change it when they first sign in."), submit button text **"Add user"**,
Cancel to Index. Title "Add user".
`Edit.cshtml`: title "Change role", shows name and email read-only, `Role` select, hidden `Id`,
`DisplayName`, `Email`, submit "Save role". Help text: "The user is signed out and picks up the new
role at their next sign-in."
`ResetPassword.cshtml`: title "Reset password", names the user, `TemporaryPassword` field, hidden `Id`
and `DisplayName`, submit "Reset password" with `hx-confirm="Reset the password for @Model.DisplayName? They are signed out at once."`
and the same `data-confirm-*` attributes as other destructive actions.
All three render `asp-validation-summary="ModelOnly"` for service errors.

- [ ] **Step 5: Nav, crumbs, CSS**

Sidebar (Admin group, before Audit log):
```csharp
new("Users", "manage_accounts", "Users", "Index", NavGroup.Admin, AtsPermission.UsersManage),
```
TopBar crumbs: `["Users"] = ("Admin", "Users")`.
`ats-components.css`, beside the other table templates:
```css
.ats-table--users { grid-template-columns: 2.4fr 1fr 1fr 1fr 64px; }
```
and add `.ats-thead.ats-table--users` to the phone `display: none` list and `.ats-table--users` to the
phone single-column list in the existing `@media (max-width: 767.98px)` block.

- [ ] **Step 6: Verify**

`dotnet test Ats.slnx` (new spot checks green, coverage test green), build, format.
`npx playwright test` (existing suite; `boosted-nav`, `a11y` and `layout-audit` include every sidebar
page, so check they still pass with the new entry). Leave the diff.

---

### Task 7: End-to-end per role

Now that non-Owner users can exist, prove the phase 1 matrix and phase 2 flows in a real browser.

**Files:**
- Create: `tests/e2e/users.spec.ts`
- Modify: `tests/e2e/rbac.spec.ts` (top comment: non-Owner coverage now lives in `users.spec.ts`)

- [ ] **Step 1: Write the spec**

`tests/e2e/users.spec.ts`:
```ts
import { test, expect, type Browser, type Page } from '@playwright/test';

// Creates real users in the local dev database (deactivated at the end). Passwords are generated per
// run and exist only in this local database.
test.describe.configure({ mode: 'serial' });

const run = Date.now();
const TEMP = `Temp-${run}-pass`;
const NEW = `New-${run}-password`;
const viewer = { name: `E2E Viewer ${run}`, email: `viewer-${run}@example.test` };
const recruiter = { name: `E2E Recruiter ${run}`, email: `recruiter-${run}@example.test` };

async function addUser(page: Page, u: { name: string; email: string }, role: string) {
  await page.goto('/Users/Create');
  await page.locator('#DisplayName').fill(u.name);
  await page.locator('#Email').fill(u.email);
  await page.locator('#Role').selectOption(role);
  await page.locator('#TemporaryPassword').fill(TEMP);
  await page.getByRole('button', { name: 'Add user' }).click();
  await expect(page.getByText(u.email)).toBeVisible();
}

async function signInAs(browser: Browser, email: string, password: string) {
  const ctx = await browser.newContext({ baseURL: test.info().project.use.baseURL, ignoreHTTPSErrors: true });
  const page = await ctx.newPage();
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  return page;
}

async function setOwnPassword(page: Page) {
  await expect(page).toHaveURL(/\/Profile\/ChangePassword/);
  await page.locator('#CurrentPassword').fill(TEMP);
  await page.locator('#NewPassword').fill(NEW);
  await page.locator('#ConfirmPassword').fill(NEW);
  await page.getByRole('button', { name: 'Change password' }).click();
  await expect(page.locator('#ats-sidebar')).toBeVisible();
}

test('owner adds a viewer and a recruiter; HiringManager is not offered', async ({ page }) => {
  await page.goto('/Users/Create');
  await expect(page.locator('#Role option')).toHaveText(['Owner', 'Recruiter', 'Viewer']);
  await addUser(page, viewer, 'Viewer');
  await addUser(page, recruiter, 'Recruiter');
});

test('viewer must set a password, then is read-only', async ({ browser }) => {
  const page = await signInAs(browser, viewer.email, TEMP);
  await expect(page).toHaveURL(/\/Profile\/ChangePassword/);
  await page.goto('/Jobs');                                  // held on the change page
  await expect(page).toHaveURL(/\/Profile\/ChangePassword/);
  await setOwnPassword(page);

  const nav = page.locator('#ats-sidebar');
  for (const hidden of ['Integrations', 'Audit log', 'Users', 'Pipelines', 'Organisation']) {
    await expect(nav.getByRole('link', { name: hidden })).toHaveCount(0);
  }
  await page.goto('/Jobs');
  await expect(page.getByRole('link', { name: 'New job' })).toHaveCount(0);
  await page.goto('/Candidates');
  await expect(page.getByRole('link', { name: 'Add candidate' })).toHaveCount(0);

  for (const url of ['/Integration', '/Audit', '/Users', '/Jobs/Create', '/Pipelines']) {
    const res = await page.goto(url);
    expect(res?.status(), url).toBe(403);
  }
  await page.context().close();
});

test('recruiter can manage jobs but not admin screens', async ({ browser }) => {
  const page = await signInAs(browser, recruiter.email, TEMP);
  await setOwnPassword(page);
  await page.goto('/Jobs');
  await expect(page.getByRole('link', { name: 'New job' })).toBeVisible();
  await expect(page.locator('#ats-sidebar').getByRole('link', { name: 'Pipelines' })).toBeVisible();
  expect((await page.goto('/Integration'))?.status()).toBe(403);
  expect((await page.goto('/Users'))?.status()).toBe(403);
  await page.context().close();
});

test('deactivating signs the user out at once', async ({ page, browser }) => {
  const viewerPage = await signInAs(browser, viewer.email, NEW);
  await expect(viewerPage.locator('#ats-sidebar')).toBeVisible();

  await page.goto('/Users');
  const row = page.locator('.ats-trow', { hasText: viewer.email });
  await row.getByRole('button', { name: /Actions for/ }).click();
  await row.getByRole('button', { name: 'Deactivate' }).click();
  await page.locator('.modal').getByRole('button', { name: 'Deactivate' }).click();   // themed confirm
  await expect(row.getByText('Deactivated')).toBeVisible();

  await viewerPage.goto('/Dashboard');
  await expect(viewerPage).toHaveURL(/\/Account\/Login/);
  await viewerPage.locator('#Email').fill(viewer.email);
  await viewerPage.locator('#Password').fill(NEW);
  await viewerPage.getByRole('button', { name: 'Sign in' }).click();
  await expect(viewerPage.getByText('This account is not available')).toBeVisible();
  await viewerPage.context().close();
});

test('owner cannot act on their own row', async ({ page }) => {
  await page.goto('/Users');
  await expect(page.locator('.ats-trow', { hasText: 'You' }).getByRole('button', { name: /Actions for/ })).toHaveCount(0);
});

test.afterAll(async ({ browser }) => {
  // Leave the recruiter deactivated so repeated runs do not pile up active test users.
  const ctx = await browser.newContext({ baseURL: test.info().project.use.baseURL, ignoreHTTPSErrors: true,
    storageState: 'tests/e2e/.auth/user.json' });
  const page = await ctx.newPage();
  await page.goto('/Users');
  const row = page.locator('.ats-trow', { hasText: recruiter.email });
  if (await row.getByRole('button', { name: /Actions for/ }).count()) {
    await row.getByRole('button', { name: /Actions for/ }).click();
    await row.getByRole('button', { name: 'Deactivate' }).click();
    await page.locator('.modal').getByRole('button', { name: 'Deactivate' }).click();
  }
  await ctx.close();
});
```
Adjust selectors to the real markup (the themed confirm modal: see `tests/e2e/confirm.ts`, which
likely already has a helper; use it). Keep every assertion.

- [ ] **Step 2: Run**

Run: `npx playwright test tests/e2e/users.spec.ts` then the full suite `npx playwright test`.
Expected: all pass. If a 403 assertion fails because the response is the re-executed status page with
a different code, report it rather than weakening the assertion.

---

### Task 8: Documentation

**Files:**
- Modify: `.claude/skills/authorization/SKILL.md`
- Modify: `.claude/rules/multi-tenancy.md`, `.claude/skills/multitenancy/SKILL.md`
- Modify: `docs/specs/2026-09-30-rbac-design.md`
- Modify: `CLAUDE.md`
- Modify: `.claude/skills/ui/SKILL.md` (only if it lists sidebar entries or crumbs explicitly)

- [ ] **Step 1: Multi-tenancy bypass list**

In both multi-tenancy files, the `IdentityService` bullet becomes:
`IdentityService.ValidateCredentialsAsync` (sign-in: no tenant claim yet; matches the unique email) and
`IdentityService.GetSessionAsync` (per-request cookie validation runs before `HttpContext.User`, so
before the tenant context; filtered explicitly by the cookie's `tenant_id` AND user id).
Keep the count of documented places accurate (it stays five places; the IdentityService place now has
two methods). In `CLAUDE.md`, the multi-tenancy line "Bypass the filter only with `IgnoreQueryFilters()`
at sign-in/onboarding" becomes "at sign-in, session validation and onboarding".

- [ ] **Step 2: Authorization skill**

Add a "Users and sessions" section: `UserService` rules (the table at the top of this plan, condensed),
`AtsRole.Assignable`, `SecurityStamp` rotation and `SessionValidator` (skips `[AllowAnonymous]`
endpoints), `MustChangePassword` with `RequirePasswordChangeFilter`, `AtsSignIn` as the only way to
issue the cookie, and `profile.manage`. Remove the known limit "A role change applies at the user's
next sign-in"; keep the HiringManager limit, reworded: "not assignable (`AtsRole.Assignable`) until
phase 3".

- [ ] **Step 3: Spec**

In `docs/specs/2026-09-30-rbac-design.md`: add `profile.manage` (all roles) to the matrix; mark phase 2
done with the temporary-password decision and "email invites later"; record the accepted last-Owner
race and that deactivation (not deletion) is the only removal.

- [ ] **Step 4: CLAUDE.md**

Skill index row for Authorization: add "users, sessions". Under Conventions, extend the authorization
bullet: "Sign-in cookies are issued only through `AtsSignIn`; any change to a user's access rotates
`SecurityStamp` (see `UserService`)."

- [ ] **Step 5: Verify**

`dotnet format Ats.slnx --verify-no-changes` clean.

---

## Definition of done (phase)
- `dotnet build Ats.slnx`: 0 errors, no new warnings
- `dotnet test Ats.slnx`: green (RolePermissions, SessionValidator, UserService, controller coverage)
- `dotnet format Ats.slnx --verify-no-changes`: clean
- `npx playwright test`: green, including `users.spec.ts`
- Migration `AddUserManagement` exists; `database update` command in the close-out
