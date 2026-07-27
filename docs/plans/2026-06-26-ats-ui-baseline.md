# ATS UI Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish a styled, consistent, best-practice MVC UI foundation for the ATS back-office (left-sidebar app shell, auth card, design tokens, shared components) and document it as a reusable skill, before Phase 1 builds feature screens.

**Architecture:** Two Razor layouts (authenticated app shell and anonymous auth card) selected via `_ViewStart`. A `SidebarNav` View Component renders data-driven navigation and the signed-in user. Shared partials (`_PageHeader`, `_Alerts`) keep pages consistent. A `site.css` design-token layer sits over the already-bundled Bootstrap 5. Client libraries (Bootstrap Icons now; htmx and SortableJS registered for Phase 1) are managed with LibMan and self-hosted, so there are no external runtime requests.

**Tech Stack:** ASP.NET Core MVC (.NET 10), Bootstrap 5, Bootstrap Icons, LibMan, htmx + SortableJS (registered, wired in Phase 1).

**Reference spec:** `docs/specs/2026-06-26-ats-ui-baseline-design.md`.

---

## Conventions for this plan

- **Verification = build + run.** There is no test project in this repo (decided earlier). Each task ends with `dotnet build` and, where relevant, a manual run check.
- **Commits are manual.** The developer runs every `git commit`. An agent executing this plan must pause and ask the developer to commit, not run it.
- **No database changes.** This plan touches no entities or migrations.
- **Working directory** for all commands is `D:\LiveProject\Ats` unless stated otherwise.
- **No em dashes and no emoji** in any generated file, per the working conventions.

---

## File structure (created or modified by this plan)

```
D:\LiveProject\Ats\
 ├─ libman.json                                          # NEW: client library manifest
 ├─ src\Ats.Web\
 │   ├─ wwwroot\css\site.css                             # MODIFY: design tokens + app-shell styles
 │   ├─ wwwroot\lib\bootstrap-icons\ ...                 # NEW (restored by LibMan)
 │   ├─ wwwroot\lib\htmx\ ...                             # NEW (restored, unused until Phase 1)
 │   ├─ wwwroot\lib\sortablejs\ ...                       # NEW (restored, unused until Phase 1)
 │   ├─ Views\_ViewImports.cshtml                        # MODIFY: add View Component tag helper
 │   ├─ Views\Shared\_Layout.cshtml                      # MODIFY: app shell (sidebar)
 │   ├─ Views\Shared\_AuthLayout.cshtml                  # NEW: centered auth card layout
 │   ├─ Views\Shared\_PageHeader.cshtml                  # NEW: page title partial
 │   ├─ Views\Shared\_Alerts.cshtml                      # NEW: TempData flash partial
 │   ├─ Views\Shared\Components\SidebarNav\Default.cshtml# NEW: sidebar markup
 │   ├─ ViewComponents\SidebarNavViewComponent.cs        # NEW: nav + current user
 │   ├─ Views\Account\_ViewStart.cshtml                  # NEW: auth pages use _AuthLayout
 │   ├─ Views\Account\Login.cshtml                       # MODIFY: Bootstrap form
 │   ├─ Views\Account\Register.cshtml                    # MODIFY: Bootstrap form
 │   ├─ Views\Dashboard\Index.cshtml                     # MODIFY: header + metric cards
 │   ├─ Controllers\HomeController.cs                    # MODIFY: redirect root to Dashboard
 │   ├─ Controllers\AccountController.cs                 # MODIFY: add Name claim on sign-in
 │   ├─ Views\Home\Index.cshtml                          # DELETE: template welcome page
 │   └─ Views\Home\Privacy.cshtml                        # DELETE: template privacy page
 ├─ src\Ats.Application\Abstractions\IIdentityService.cs # MODIFY: SignInResult gains DisplayName
 ├─ src\Ats.Infrastructure\Identity\IdentityService.cs   # MODIFY: return DisplayName
 ├─ CLAUDE.md                                            # MODIFY: skill-index + front-end note
 ├─ .claude\skills\architecture\SKILL.md                 # MODIFY: reference UI skill
 └─ .claude\skills\ui\SKILL.md                           # NEW: UI conventions skill
```

---

## Task 1: Add client libraries with LibMan

**Files:**
- Create: `libman.json`.
- Restored into: `src/Ats.Web/wwwroot/lib/bootstrap-icons`, `.../htmx`, `.../sortablejs`.

- [ ] **Step 1: Ensure the LibMan CLI is installed** (dev tooling; allowed)

```bash
dotnet tool install --global Microsoft.Web.LibraryManager.Cli
```
If it is already installed, this is a no-op or reports it exists. (Update with `dotnet tool update --global Microsoft.Web.LibraryManager.Cli` if needed.)

- [ ] **Step 2: Create `libman.json` at the repo root**

```json
{
  "version": "1.0",
  "defaultProvider": "unpkg",
  "libraries": [
    {
      "library": "bootstrap-icons@1.11.3",
      "destination": "src/Ats.Web/wwwroot/lib/bootstrap-icons",
      "files": [
        "font/bootstrap-icons.min.css",
        "font/fonts/bootstrap-icons.woff",
        "font/fonts/bootstrap-icons.woff2"
      ]
    },
    {
      "library": "htmx.org@2.0.4",
      "destination": "src/Ats.Web/wwwroot/lib/htmx",
      "files": [ "dist/htmx.min.js" ]
    },
    {
      "library": "sortablejs@1.15.6",
      "destination": "src/Ats.Web/wwwroot/lib/sortablejs",
      "files": [ "Sortable.min.js" ]
    }
  ]
}
```

- [ ] **Step 3: Restore the libraries** (downloads to `wwwroot/lib`)

```bash
cd /d/LiveProject/Ats
libman restore
```
Expected: "Restore operation completed" and the three folders appear under `src/Ats.Web/wwwroot/lib`.

- [ ] **Step 4: Verify the icon CSS and font landed**

```bash
ls src/Ats.Web/wwwroot/lib/bootstrap-icons/font/bootstrap-icons.min.css
ls src/Ats.Web/wwwroot/lib/bootstrap-icons/font/fonts/
ls src/Ats.Web/wwwroot/lib/htmx/dist/htmx.min.js
ls src/Ats.Web/wwwroot/lib/sortablejs/Sortable.min.js
```
Expected: all paths exist.

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit** (developer runs this)

```bash
git add -A
git commit -m "chore(web): add bootstrap-icons, htmx, sortablejs via LibMan"
```

---

## Task 2: Design tokens and app-shell styles in site.css

**Files:**
- Modify: `src/Ats.Web/wwwroot/css/site.css` (replace its contents).

- [ ] **Step 1: Replace `src/Ats.Web/wwwroot/css/site.css` with the token layer and shell styles**

```css
:root {
  --ats-accent: #4f46e5;
  --ats-accent-hover: #4338ca;
  --ats-sidebar-bg: #1f2937;
  --ats-sidebar-fg: #e5e7eb;
  --ats-sidebar-fg-muted: #9ca3af;
  --ats-sidebar-active-bg: #374151;
  --ats-content-bg: #f6f7f9;
  --ats-border: #e5e7eb;
  --ats-radius: .5rem;
}

html, body { height: 100%; }
body { background: var(--ats-content-bg); }

.ats-shell { display: flex; min-height: 100vh; }

.ats-sidebar {
  width: 220px; flex: 0 0 220px;
  background: var(--ats-sidebar-bg); color: var(--ats-sidebar-fg);
  display: flex; flex-direction: column; padding: 1rem .75rem;
}
.ats-brand {
  font-size: 1.15rem; font-weight: 600; color: #fff;
  display: flex; align-items: center; gap: .5rem; padding: .25rem .5rem 1rem;
}
.ats-nav { display: flex; flex-direction: column; gap: .25rem; flex: 1; }
.ats-nav a {
  color: var(--ats-sidebar-fg-muted); text-decoration: none;
  display: flex; align-items: center; gap: .6rem;
  padding: .5rem .65rem; border-radius: var(--ats-radius); font-size: .925rem;
}
.ats-nav a:hover { color: #fff; background: var(--ats-sidebar-active-bg); }
.ats-nav a.active { color: #fff; background: var(--ats-accent); }
.ats-sidebar-footer {
  border-top: 1px solid rgba(255,255,255,.1);
  padding-top: .75rem; font-size: .85rem; color: var(--ats-sidebar-fg-muted);
}

.ats-content { flex: 1; padding: 1.5rem 2rem; min-width: 0; }

.ats-auth-wrap {
  min-height: 100vh; display: flex; align-items: center; justify-content: center;
  background: var(--ats-content-bg); padding: 1rem;
}
.ats-auth-card { width: 100%; max-width: 400px; }
.ats-auth-brand { text-align: center; font-size: 1.4rem; font-weight: 600; margin-bottom: 1rem; }

.btn-primary {
  --bs-btn-bg: var(--ats-accent);
  --bs-btn-border-color: var(--ats-accent);
  --bs-btn-hover-bg: var(--ats-accent-hover);
  --bs-btn-hover-border-color: var(--ats-accent-hover);
  --bs-btn-active-bg: var(--ats-accent-hover);
  --bs-btn-active-border-color: var(--ats-accent-hover);
}
a { color: var(--ats-accent); }
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "style(web): add design tokens and app-shell styles"
```

---

## Task 3: SignInResult gains DisplayName (so the sidebar can show the user)

**Files:**
- Modify: `src/Ats.Application/Abstractions/IIdentityService.cs`.
- Modify: `src/Ats.Infrastructure/Identity/IdentityService.cs`.

- [ ] **Step 1: Update the `SignInResult` record in `IIdentityService.cs`**

Replace the record line with:

```csharp
public record SignInResult(bool Succeeded, int? UserId, int? TenantId, string? Role, string? DisplayName, string? Error);
```

- [ ] **Step 2: Update the two returns in `IdentityService.ValidateCredentialsAsync`**

Replace the failure and success returns with:

```csharp
        if (user is null || !VerifyPassword(user.PasswordHash, password))
            return new SignInResult(false, null, null, null, null, "Invalid email or password.");

        return new SignInResult(true, user.Id, user.TenantId, user.Role, user.DisplayName, null);
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: 2 errors in `AccountController.cs` (the `Login` action constructs the old `SignInResult` shape and calls `SignInAsync` with the old signature). These are fixed in Task 4. If you want a green build at this exact step, do Task 4 before building. Otherwise proceed to Task 4 now.

- [ ] **Step 4: Commit** (developer; commit together with Task 4 if you prefer a green checkpoint)

```bash
git add -A
git commit -m "feat: add DisplayName to SignInResult"
```

---

## Task 4: AccountController adds a Name claim on sign-in

**Files:**
- Modify: `src/Ats.Web/Controllers/AccountController.cs`.

- [ ] **Step 1: Update `SignInAsync` to accept and set a display-name claim**

Replace the private `SignInAsync` method with:

```csharp
    private async Task SignInAsync(int userId, int tenantId, string role, string displayName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, role),
            new("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "AtsCookie");
        await HttpContext.SignInAsync("AtsCookie", new ClaimsPrincipal(identity));
    }
```

- [ ] **Step 2: Update the `Register` action's sign-in call**

In the `Register` POST action, replace the sign-in line with:

```csharp
        await SignInAsync(result.OwnerUserId, result.TenantId, "Owner", vm.OwnerName.Trim());
        return RedirectToAction("Index", "Dashboard");
```

- [ ] **Step 3: Update the `Login` action's sign-in call**

In the `Login` POST action, replace the sign-in line with:

```csharp
        await SignInAsync(result.UserId!.Value, result.TenantId!.Value, result.Role!, result.DisplayName ?? "");
        return RedirectToAction("Index", "Dashboard");
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): set Name claim on sign-in for the app shell"
```

---

## Task 5: Shared components (View Component + partials)

**Files:**
- Create: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`.
- Create: `src/Ats.Web/Views/Shared/Components/SidebarNav/Default.cshtml`.
- Create: `src/Ats.Web/Views/Shared/_PageHeader.cshtml`.
- Create: `src/Ats.Web/Views/Shared/_Alerts.cshtml`.
- Modify: `src/Ats.Web/Views/_ViewImports.cshtml`.

- [ ] **Step 1: Create `SidebarNavViewComponent.cs`**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public record NavItem(string Text, string Icon, string Controller, string Action);

public record SidebarNavModel(IReadOnlyList<NavItem> Items, string CurrentController, string UserName, string Role);

public class SidebarNavViewComponent : ViewComponent
{
    // Phase 1+ appends Jobs, Pipelines, Candidates, Settings here.
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "bi-speedometer2", "Dashboard", "Index"),
    };

    public IViewComponentResult Invoke()
    {
        var current = RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var name = User.Identity?.Name is { Length: > 0 } n ? n : "User";
        var role = (User as ClaimsPrincipal)?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        return View(new SidebarNavModel(Items, current, name, role));
    }
}
```

- [ ] **Step 2: Create the View Component view `Views/Shared/Components/SidebarNav/Default.cshtml`**

```cshtml
@model Ats.Web.ViewComponents.SidebarNavModel
<aside class="ats-sidebar">
    <div class="ats-brand"><i class="bi bi-briefcase-fill"></i> ATS</div>
    <nav class="ats-nav">
        @foreach (var item in Model.Items)
        {
            var active = string.Equals(item.Controller, Model.CurrentController, StringComparison.OrdinalIgnoreCase) ? "active" : "";
            <a class="@active" asp-controller="@item.Controller" asp-action="@item.Action">
                <i class="bi @item.Icon"></i> @item.Text
            </a>
        }
    </nav>
    <div class="ats-sidebar-footer">
        <div><i class="bi bi-person-circle"></i> @Model.UserName</div>
        @if (!string.IsNullOrEmpty(Model.Role))
        {
            <div class="text-uppercase" style="font-size:.7rem; letter-spacing:.04em;">@Model.Role</div>
        }
        <form asp-controller="Account" asp-action="Logout" method="post" class="mt-2">
            <button type="submit" class="btn btn-sm btn-outline-light w-100">Sign out</button>
        </form>
    </div>
</aside>
```

- [ ] **Step 3: Create `Views/Shared/_PageHeader.cshtml`** (reads `ViewData["Title"]`)

```cshtml
<div class="d-flex align-items-center justify-content-between mb-4">
    <h1 class="h3 mb-0">@ViewData["Title"]</h1>
</div>
```

- [ ] **Step 4: Create `Views/Shared/_Alerts.cshtml`** (TempData flash messages)

```cshtml
@{
    var success = TempData["Success"] as string;
    var error = TempData["Error"] as string;
    var info = TempData["Info"] as string;
}
@if (!string.IsNullOrEmpty(success)) { <div class="alert alert-success" role="alert">@success</div> }
@if (!string.IsNullOrEmpty(error)) { <div class="alert alert-danger" role="alert">@error</div> }
@if (!string.IsNullOrEmpty(info)) { <div class="alert alert-info" role="alert">@info</div> }
```

- [ ] **Step 5: Add the View Component tag helper to `Views/_ViewImports.cshtml`**

Append this line (keep the existing lines):

```cshtml
@addTagHelper *, Ats.Web
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (The new files compile even though `_Layout` does not use them yet.)

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): add SidebarNav view component and shared partials"
```

---

## Task 6: App-shell layout (_Layout.cshtml)

**Files:**
- Modify: `src/Ats.Web/Views/Shared/_Layout.cshtml` (replace its contents).

- [ ] **Step 1: Replace `_Layout.cshtml` with the sidebar shell**

```cshtml
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - ATS</title>
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/bootstrap-icons/font/bootstrap-icons.min.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/Ats.Web.styles.css" asp-append-version="true" />
</head>
<body>
    <div class="ats-shell">
        <vc:sidebar-nav></vc:sidebar-nav>
        <main class="ats-content">
            <partial name="_Alerts" />
            <partial name="_PageHeader" />
            @RenderBody()
        </main>
    </div>
    <script src="~/lib/jquery/dist/jquery.min.js"></script>
    <script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): app-shell layout with left sidebar"
```

---

## Task 7: Auth layout and restyled Login/Register

**Files:**
- Create: `src/Ats.Web/Views/Shared/_AuthLayout.cshtml`.
- Create: `src/Ats.Web/Views/Account/_ViewStart.cshtml`.
- Modify: `src/Ats.Web/Views/Account/Login.cshtml`.
- Modify: `src/Ats.Web/Views/Account/Register.cshtml`.

- [ ] **Step 1: Create `Views/Shared/_AuthLayout.cshtml`**

```cshtml
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - ATS</title>
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/bootstrap-icons/font/bootstrap-icons.min.css" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
</head>
<body>
    <div class="ats-auth-wrap">
        <div class="ats-auth-card">
            <div class="ats-auth-brand"><i class="bi bi-briefcase-fill"></i> ATS</div>
            <div class="card shadow-sm">
                <div class="card-body p-4">
                    @RenderBody()
                </div>
            </div>
        </div>
    </div>
    <script src="~/lib/jquery/dist/jquery.min.js"></script>
    <script src="~/lib/jquery-validation/dist/jquery.validate.min.js"></script>
    <script src="~/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

- [ ] **Step 2: Create `Views/Account/_ViewStart.cshtml`** (auth pages use the auth layout)

```cshtml
@{
    Layout = "_AuthLayout";
}
```

- [ ] **Step 3: Replace `Views/Account/Login.cshtml`**

```cshtml
@model Ats.Web.Models.LoginViewModel
@{ ViewData["Title"] = "Sign in"; }
<h1 class="h4 mb-3 text-center">Sign in</h1>
<form asp-action="Login" method="post">
    <div asp-validation-summary="ModelOnly" class="text-danger small mb-2"></div>
    <div class="mb-3">
        <label asp-for="Email" class="form-label">Email</label>
        <input asp-for="Email" class="form-control" autocomplete="username" />
        <span asp-validation-for="Email" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Password" class="form-label">Password</label>
        <input asp-for="Password" class="form-control" autocomplete="current-password" />
        <span asp-validation-for="Password" class="text-danger small"></span>
    </div>
    <button type="submit" class="btn btn-primary w-100">Sign in</button>
</form>
<p class="text-center mt-3 mb-0 small">Need an account? <a asp-action="Register">Create one</a></p>
```

- [ ] **Step 4: Replace `Views/Account/Register.cshtml`**

```cshtml
@model Ats.Web.Models.RegisterViewModel
@{ ViewData["Title"] = "Create account"; }
<h1 class="h4 mb-3 text-center">Create your company account</h1>
<form asp-action="Register" method="post">
    <div asp-validation-summary="ModelOnly" class="text-danger small mb-2"></div>
    <div class="mb-3">
        <label asp-for="CompanyName" class="form-label">Company name</label>
        <input asp-for="CompanyName" class="form-control" />
        <span asp-validation-for="CompanyName" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Slug" class="form-label">URL slug</label>
        <div class="input-group">
            <input asp-for="Slug" class="form-control" />
            <span class="input-group-text">.ourats.com/careers</span>
        </div>
        <span asp-validation-for="Slug" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="OwnerName" class="form-label">Your name</label>
        <input asp-for="OwnerName" class="form-control" />
        <span asp-validation-for="OwnerName" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="OwnerEmail" class="form-label">Work email</label>
        <input asp-for="OwnerEmail" class="form-control" autocomplete="email" />
        <span asp-validation-for="OwnerEmail" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Password" class="form-label">Password</label>
        <input asp-for="Password" class="form-control" autocomplete="new-password" />
        <span asp-validation-for="Password" class="text-danger small"></span>
    </div>
    <button type="submit" class="btn btn-primary w-100">Create account</button>
</form>
<p class="text-center mt-3 mb-0 small">Already have an account? <a asp-action="Login">Sign in</a></p>
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run and verify the auth card**

Run: `dotnet run --project src/Ats.Web`
Browse (https): `/Account/Login` and `/Account/Register`.
Expected: centered card with the ATS wordmark, styled inputs and primary button, and client-side validation firing on empty submit. Stop the app.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): auth-card layout and restyled login/register"
```

---

## Task 8: Restyle the Dashboard

**Files:**
- Modify: `src/Ats.Web/Views/Dashboard/Index.cshtml`.

- [ ] **Step 1: Replace `Views/Dashboard/Index.cshtml`** (header comes from `_PageHeader`; placeholder uses a plain hyphen, not an em dash)

```cshtml
@{ ViewData["Title"] = "Dashboard"; }
<div class="row g-3 mb-4">
    <div class="col-sm-4">
        <div class="card h-100"><div class="card-body">
            <div class="text-muted small">Open jobs</div>
            <div class="fs-3 fw-semibold">-</div>
        </div></div>
    </div>
    <div class="col-sm-4">
        <div class="card h-100"><div class="card-body">
            <div class="text-muted small">Candidates</div>
            <div class="fs-3 fw-semibold">-</div>
        </div></div>
    </div>
    <div class="col-sm-4">
        <div class="card h-100"><div class="card-body">
            <div class="text-muted small">In pipeline</div>
            <div class="fs-3 fw-semibold">-</div>
        </div></div>
    </div>
</div>
<p class="text-muted">Jobs, pipelines, and candidates arrive in Phase 1.</p>
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): dashboard header and placeholder metric cards"
```

---

## Task 9: Routing cleanup (root redirect, remove template pages)

**Files:**
- Modify: `src/Ats.Web/Controllers/HomeController.cs`.
- Delete: `src/Ats.Web/Views/Home/Index.cshtml`, `src/Ats.Web/Views/Home/Privacy.cshtml`.

- [ ] **Step 1: Replace `HomeController.cs` so `/` redirects to the Dashboard**

Read the existing file first to keep the `Error` action and its using directives. Replace the controller body so it reads:

```csharp
using System.Diagnostics;
using Ats.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Dashboard");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
```

> If `ErrorViewModel` lives in a different namespace in this project, keep the existing `using` for it rather than the one shown. The goal is only to change `Index` to a redirect and drop the `Privacy` action.

- [ ] **Step 2: Delete the template welcome and privacy views**

```bash
rm -f src/Ats.Web/Views/Home/Index.cshtml src/Ats.Web/Views/Home/Privacy.cshtml
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run and verify routing**

Run: `dotnet run --project src/Ats.Web`
- Signed out, browse `/` (https): expected redirect to `/Account/Login`.
- Sign in, then browse `/`: expected redirect to `/Dashboard`, rendered inside the sidebar shell with the signed-in name and role in the sidebar footer, and Sign out working.
Stop the app.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): redirect root to dashboard and remove template pages"
```

---

## Task 10: UI conventions skill and knowledge-base updates

**Files:**
- Create: `.claude/skills/ui/SKILL.md`.
- Modify: `CLAUDE.md` (skill-index row + front-end note).
- Modify: `.claude/skills/architecture/SKILL.md` (reference the UI skill).

- [ ] **Step 1: Create `.claude/skills/ui/SKILL.md`**

```markdown
---
name: ui
description: The Ats back-office UI pattern - layouts, design tokens, shared components, and the exact steps to add a new page consistently. Read before building or changing any view.
---

# Ats UI

## Stack
Server-rendered ASP.NET Core MVC + Bootstrap 5 (self-hosted in `wwwroot/lib`). Icons: Bootstrap
Icons (`<i class="bi bi-..."></i>`). Client libraries are managed in `libman.json`; run
`libman restore` to hydrate `wwwroot/lib`. htmx + SortableJS are present for the Phase 1 kanban.
No external/CDN requests at runtime.

## Layouts
- `Views/Shared/_Layout.cshtml` - authenticated app shell: left sidebar (`<vc:sidebar-nav>`) plus a
  content area that renders `_Alerts`, `_PageHeader`, then the body. Default for all back-office pages.
- `Views/Shared/_AuthLayout.cshtml` - centered card for anonymous pages (login, register).
- Layout selection is declarative: `Views/_ViewStart.cshtml` sets `_Layout`;
  `Views/Account/_ViewStart.cshtml` overrides to `_AuthLayout`.

## Design tokens
All theming lives in `wwwroot/css/site.css` as CSS custom properties (`--ats-accent`, sidebar
colors, spacing, radius) plus a few Bootstrap overrides. Change the accent in one place. Do not put
theme colors inline in views.

## Shared components
- `SidebarNavViewComponent` (`ViewComponents/`) renders the nav from an in-code `NavItem[]`, marks
  the active item from the current controller, and shows the signed-in user (`Name` claim) and role.
  Add a new section by appending a `NavItem`.
- `_PageHeader.cshtml` renders the page title from `ViewData["Title"]`. Every content page sets
  `ViewData["Title"]`.
- `_Alerts.cshtml` renders `TempData["Success"|"Error"|"Info"]` as Bootstrap alerts. Set those in
  controllers for post-redirect feedback.

## Forms
Use tag helpers: `asp-for`, `asp-validation-for`, `asp-validation-summary="ModelOnly"`. Inputs get
`class="form-control"`, primary action `class="btn btn-primary"`. Client-side validation is wired in
`_AuthLayout`; for back-office forms add `<partial name="_ValidationScriptsPartial" />` in a
`@section Scripts`.

## Add a new back-office page (checklist)
1. Controller action returns `View(...)`; the page is `Views/<Controller>/<Action>.cshtml`.
2. First line: `@{ ViewData["Title"] = "..."; }` (drives the header and browser title).
3. Use Bootstrap layout (`row`, `col-*`, `card`) for content. No inline theme colors.
4. To surface in the sidebar, append a `NavItem` in `SidebarNavViewComponent`.
5. For flash messages, set `TempData["Success"]` etc. in the action.

## Security
Antiforgery is validated globally (`AutoValidateAntiforgeryToken`); form tag helpers emit the token.
The auth cookie is `HttpOnly` + `Secure`, so test over https.
```

- [ ] **Step 2: Update `CLAUDE.md`**

Add a "UI" row to the skill-index table (after the Multi-tenancy row):

```markdown
| UI | `.claude/skills/ui/SKILL.md` | Layouts, design tokens, shared components, how to add a page |
```

And add a "Front-end" subsection under Build / run:

```markdown
## Front-end
Server-rendered MVC + Bootstrap 5. Client libraries are managed by LibMan (`libman.json`); run
`libman restore` to populate `src/Ats.Web/wwwroot/lib`. UI conventions: `.claude/skills/ui/SKILL.md`.
```

- [ ] **Step 3: Reference the UI skill from `.claude/skills/architecture/SKILL.md`**

Under "Where things go", add:

```markdown
- New back-office page or view -> follow `.claude/skills/ui/SKILL.md` (layouts, tokens, components).
```

- [ ] **Step 4: Build** (docs only; confirms nothing broke)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "docs: add UI skill and update CLAUDE.md skill-index"
```

---

## Task 11: Manual end-to-end UI verification

**No new files.** Confirms the baseline looks and behaves correctly.

- [ ] **Step 1: Run the app**

Run: `dotnet run --project src/Ats.Web` (use the https URL).

- [ ] **Step 2: Anonymous flow**

Browse `/`. Expected: redirect to `/Account/Login`, shown as a centered card with the ATS wordmark.
Submit empty: client-side validation messages appear.

- [ ] **Step 3: Register and shell**

Open `/Account/Register`, register a new company. Expected: redirect to `/Dashboard` rendered in the
sidebar shell; the sidebar footer shows your name and role; Dashboard shows the three metric cards.

- [ ] **Step 4: Active nav and sign out**

Confirm the Dashboard nav item is highlighted (active). Click Sign out. Expected: returned to Login.

- [ ] **Step 5: Consistency check**

Confirm login, register, and dashboard share the same wordmark, accent color, fonts, and spacing, and
that no page shows the old template Welcome or Privacy chrome.

- [ ] **Step 6: Knowledge-base gate**

Confirm `.claude/skills/ui/SKILL.md` exists and matches what was built, and that `CLAUDE.md`'s
skill-index lists the UI skill.

- [ ] **Step 7: Final commit** (developer)

```bash
git add -A
git commit -m "chore: UI baseline complete and verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage:** two layouts (Tasks 6, 7); SidebarNav view component + `_PageHeader` + `_Alerts` (Task 5); design tokens in site.css (Task 2); Bootstrap Icons + htmx + SortableJS via LibMan, self-hosted (Task 1); restyled Login/Register (Task 7) and Dashboard (Task 8); routing cleanup with root redirect and template-page removal (Task 9); MVC best practices (view component, partials, declarative `_ViewStart`, tag helpers, LibMan) across Tasks 1, 5, 6, 7; UI skill + CLAUDE.md skill-index + architecture reference (Task 10); verification (Task 11). The sidebar needed a display name, so SignInResult/AccountController were extended (Tasks 3, 4) - a small, spec-consistent addition for the "current user" element.
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact replacement content.
- **Type consistency:** `SignInResult(bool, int?, int?, string?, string?, string?)` defined in Task 3 and consumed in Task 4; `SignInAsync(int, int, string, string)` defined and called consistently in Task 4; `SidebarNavModel`/`NavItem` defined and used in Task 5; `<vc:sidebar-nav>` requires the `@addTagHelper *, Ats.Web` added in Task 5 and is used in Task 6.
- **Ordering:** Task 3 leaves a transient compile error fixed in Task 4; commit those two together for a green checkpoint, as noted. All other tasks build green on their own.
```
