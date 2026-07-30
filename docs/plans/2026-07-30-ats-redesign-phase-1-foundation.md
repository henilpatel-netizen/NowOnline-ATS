# ATS NowOnline Redesign - Phase 1: Foundation and Shell

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking. Work them in order.
>
> **Two deviations from the standard plan format, both mandated by this repo:**
> 1. **No `git` steps.** `.claude/rules/restrictions.md` forbids the AI running commit/push/merge.
>    Each task ends with a **Verify** step instead. Commit points are marked; the developer commits.
> 2. **No `dotnet ef database update`.** The migration *file* is created (allowed); applying it is
>    manual. Tasks after the migration cannot be runtime-verified until the developer applies it.
>
> **Source of truth for visual detail:** `ATS - Redesign.dc.html` in the handoff bundle at
> `%TEMP%\claude\D--LiveProject-Ats\<session>\scratchpad\handoff\ats-redesign-and-improvements\project\`.
> Line references below point into that file. Design tokens come from
> `_ds/nowonline-design-system-.../colors_and_type.css` in the same bundle.

**Goal:** Land the NowOnline design system, the per-tenant branding pipeline, and the new app shell
(sidebar, topbar, layouts, shared partials) so that every existing screen renders in the new visual
language with all current behaviour intact.

**Architecture:** A layered stylesheet built on NowOnline custom properties overrides Bootstrap 5's
variables rather than replacing Bootstrap. Per-tenant branding lives on `TenantSettings`, is resolved
by a request-scoped `ITenantBrandingService`, and is emitted as CSS custom properties by a view
component on the shell root. Reusable presentation lives in `Views/Shared/Partials` and three view
components; screen-specific markup stays in its own view.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (SQL Server), Bootstrap 5, htmx, SortableJS,
LibMan, Material Symbols Outlined, xUnit 2.9.3.

**Spec:** `docs/specs/2026-07-30-ats-nowonline-redesign-design.md`

**Phase sequence.** This plan is Phase 1 of four. Later phases get their own plan documents, written
once the phase before them lands:

| Phase | Scope |
|---|---|
| 1 (this plan) | design tokens, fonts, icons, branding schema + service, shell, shared partials, global search, icon sweep |
| 2 | dashboard, jobs list, board, candidates, candidate drawer, and their read models |
| 3 | pipelines, organisation, integrations, audit, career-site back office, branding screen |
| 4 | public career site |

All schema changes for all four phases are in Phase 1 Task 2 as a single migration, so the developer
applies the database once rather than four times.

---

## File Structure

### Created

| File | Responsibility |
|---|---|
| `tests/Ats.Tests/Ats.Tests.csproj` | xUnit project, references Domain + Application |
| `tests/Ats.Tests/Branding/BrandColorTests.cs` | accent validation and hover derivation |
| `tests/Ats.Tests/Presentation/AvatarTests.cs` | initials and deterministic colour pair |
| `tests/Ats.Tests/Presentation/RelativeTimeTests.cs` | long and short age formatting |
| `src/Ats.Domain/Enums/SidebarTheme.cs` | `Dark`/`Light` |
| `src/Ats.Domain/Enums/ApplicationOrigin.cs` | `Unknown`/`CareerSite`/`Manual`/`Referral` |
| `src/Ats.Application/Branding/TenantBranding.cs` | resolved branding record + `BrandColor` helper |
| `src/Ats.Application/Branding/ITenantBrandingService.cs` | branding read/write contract |
| `src/Ats.Application/Common/AvatarPalette.cs` | initials + colour pair, pure |
| `src/Ats.Application/Common/RelativeTime.cs` | age formatting, pure |
| `src/Ats.Application/Shell/IShellSummaryService.cs` | nav counts + bell state |
| `src/Ats.Application/Shell/ShellSummary.cs` | nav counts record |
| `src/Ats.Application/Search/IGlobalSearchService.cs` | cross-entity search contract |
| `src/Ats.Application/Search/SearchResults.cs` | search result records |
| `src/Ats.Infrastructure/Branding/TenantBrandingService.cs` | EF branding read/write, request-cached |
| `src/Ats.Infrastructure/Shell/ShellSummaryService.cs` | EF nav counts |
| `src/Ats.Infrastructure/Search/GlobalSearchService.cs` | EF cross-entity search |
| `src/Ats.Infrastructure/Persistence/Configurations/TenantSettingsConfiguration.cs` | max lengths for the new columns |
| `src/Ats.Web/ViewComponents/BrandingViewComponent.cs` | emits CSS custom properties |
| `src/Ats.Web/ViewComponents/TopBarViewComponent.cs` | breadcrumb, search, bell, action |
| `src/Ats.Web/Controllers/SearchController.cs` | `GET /Search?q=` returns a partial |
| `src/Ats.Web/Models/SearchViewModel.cs` | search view model |
| `src/Ats.Web/wwwroot/css/ats-tokens.css` | NowOnline tokens + Bootstrap overrides |
| `src/Ats.Web/wwwroot/css/ats-base.css` | `@font-face`, typography, `.ms` icon class |
| `src/Ats.Web/wwwroot/css/ats-components.css` | reusable component classes |
| `src/Ats.Web/wwwroot/css/ats-shell.css` | sidebar, topbar, content, drawer host |
| `src/Ats.Web/wwwroot/lib/nowonline-fonts/*.ttf` | 5 variable fonts from the bundle |
| `src/Ats.Web/Views/Shared/Components/Branding/Default.cshtml` | style element |
| `src/Ats.Web/Views/Shared/Components/TopBar/Default.cshtml` | topbar markup |
| `src/Ats.Web/Views/Shared/Partials/_Avatar.cshtml` | initials avatar |
| `src/Ats.Web/Views/Shared/Partials/_StatTile.cshtml` | KPI tile |
| `src/Ats.Web/Views/Shared/Partials/_StatusPill.cshtml` | dot + label pill |
| `src/Ats.Web/Views/Shared/Partials/_SourceChip.cshtml` | origin chip |
| `src/Ats.Web/Views/Shared/Partials/_PipelineBar.cshtml` | segmented stage bar |
| `src/Ats.Web/Views/Shared/Partials/_EmptyState.cshtml` | empty state block |
| `src/Ats.Web/Views/Shared/Partials/_Timeline.cshtml` | dotted vertical timeline |
| `src/Ats.Web/Views/Search/_Results.cshtml` | search dropdown results |
| `src/Ats.Web/Models/Shared/*.cs` | view models for the partials above |

### Modified

| File | Change |
|---|---|
| `Ats.slnx` | add the test project |
| `src/Ats.Domain/Entities/TenantSettings.cs` | 6 branding/feed columns |
| `src/Ats.Domain/Entities/JobApplication.cs` | `Origin` |
| `src/Ats.Infrastructure/DependencyInjection.cs` | register 3 new services |
| `src/Ats.Web/Views/Shared/_Layout.cshtml` | rewritten shell, and host for the page-head markup |
| `src/Ats.Web/Views/Shared/_AuthLayout.cshtml` | rewritten auth shell |
| `src/Ats.Web/Views/Shared/_Alerts.cshtml` | restyled |
| `src/Ats.Web/Views/Shared/_Pager.cshtml` | pill pagination |
| `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs` | groups, badges, branding |
| `src/Ats.Web/Views/Shared/Components/SidebarNav/Default.cshtml` | rewritten |
| `libman.json` | add `material-symbols`, drop `bootstrap-icons` |
| all views containing `bi-*` | swap to Material Symbols |
| `src/Ats.Web/wwwroot/css/site.css` | deleted, superseded by the four `ats-*.css` files |

### Deleted

`src/Ats.Web/wwwroot/lib/bootstrap-icons/` (3 tracked files) and `src/Ats.Web/wwwroot/css/site.css`.

---

## Task 1: Test project

**Files:**
- Create: `tests/Ats.Tests/Ats.Tests.csproj`
- Modify: `Ats.slnx`

- [ ] **Step 1: Create the project file**

`tests/Ats.Tests/Ats.Tests.csproj`. Versions are pinned to what is already in the local NuGet cache
so restore works offline.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Ats.Domain/Ats.Domain.csproj" />
    <ProjectReference Include="../../src/Ats.Application/Ats.Application.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add it to the solution**

Add a `/tests/` folder entry to `Ats.slnx`, keeping the existing `/src/` folder untouched:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Ats.Api/Ats.Api.csproj" />
    <Project Path="src/Ats.Application/Ats.Application.csproj" />
    <Project Path="src/Ats.Domain/Ats.Domain.csproj" />
    <Project Path="src/Ats.Infrastructure/Ats.Infrastructure.csproj" />
    <Project Path="src/Ats.Web/Ats.Web.csproj" />
    <Project Path="src/Ats.Worker/Ats.Worker.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Ats.Tests/Ats.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 3: Verify restore and build**

Run: `dotnet build`
Expected: success, 7 projects. If restore reaches for the network and fails, add
`--source "%USERPROFILE%\.nuget\packages"` is *not* valid for restore; instead confirm the cache hit
with `dotnet restore tests/Ats.Tests/Ats.Tests.csproj` and report the failure rather than changing
package versions.

- [ ] **Step 4: Verify the test runner works**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: success with "no test source files" or zero tests. Not a failure.

*Commit point: `chore: add Ats.Tests project`*

---

## Task 2: Schema for all four phases

One migration covering every column any phase needs, so the developer applies the database once.

**Files:**
- Create: `src/Ats.Domain/Enums/SidebarTheme.cs`
- Create: `src/Ats.Domain/Enums/ApplicationOrigin.cs`
- Create: `src/Ats.Infrastructure/Persistence/Configurations/TenantSettingsConfiguration.cs`
- Modify: `src/Ats.Domain/Entities/TenantSettings.cs`
- Modify: `src/Ats.Domain/Entities/JobApplication.cs`

- [ ] **Step 1: Add the two enums**

`src/Ats.Domain/Enums/SidebarTheme.cs`:

```csharp
namespace Ats.Domain.Enums;

public enum SidebarTheme
{
    Dark = 0,
    Light = 1
}
```

`src/Ats.Domain/Enums/ApplicationOrigin.cs`:

```csharp
namespace Ats.Domain.Enums;

// How an application entered the system. Presentation only: never read by the outbox,
// the worker, the vacancy feed, or the ReferralTool client.
public enum ApplicationOrigin
{
    Unknown = 0,
    CareerSite = 1,
    Manual = 2,
    Referral = 3
}
```

- [ ] **Step 2: Add the branding and feed columns to `TenantSettings`**

Append to the existing property list in `src/Ats.Domain/Entities/TenantSettings.cs` (keep every
current property and the `using` for `Ats.Domain.Common`; add `using Ats.Domain.Enums;`):

```csharp
    // Branding (drives the redesign's white-label props). Null means "use the NowOnline default",
    // so an existing tenant renders identically until someone sets a value.
    public string? BrandAccentColor { get; set; }
    public SidebarTheme? BrandSidebarTheme { get; set; }
    public string? CareerHeroHeadline { get; set; }
    public string? CareerHeroHeadlineOutlined { get; set; }
    public string? CareerHeroIntro { get; set; }

    // Telemetry for the integration health panels. Written by the vacancy feed endpoint.
    public DateTimeOffset? FeedLastPulledAt { get; set; }
```

- [ ] **Step 3: Add `Origin` to `JobApplication`**

In `src/Ats.Domain/Entities/JobApplication.cs`, add after `SourceCode`:

```csharp
    public ApplicationOrigin Origin { get; set; } = ApplicationOrigin.Unknown;
```

- [ ] **Step 4: Configure the new column lengths**

`src/Ats.Infrastructure/Persistence/Configurations/TenantSettingsConfiguration.cs`. `TenantSettings`
had no configuration class before; it was conventional. Creating one changes nothing about the
existing columns because only the new properties are configured.

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> b)
    {
        b.Property(s => s.BrandAccentColor).HasMaxLength(9);
        b.Property(s => s.CareerHeroHeadline).HasMaxLength(160);
        b.Property(s => s.CareerHeroHeadlineOutlined).HasMaxLength(160);
        b.Property(s => s.CareerHeroIntro).HasMaxLength(600);
    }
}
```

- [ ] **Step 5: Create the migration file**

Run:

```bash
dotnet ef migrations add AddBrandingAndApplicationOrigin --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

Expected: `Done.` and three new files under `src/Ats.Infrastructure/Migrations/`.

- [ ] **Step 6: Review the generated migration**

Read the generated `*_AddBrandingAndApplicationOrigin.cs`. Confirm it contains exactly seven
`AddColumn` calls and nothing else. It must have **no** `DropColumn`, `AlterColumn`, `DropIndex`, or
`Sql(...)` call. `Origin` must be `nullable: false` with `defaultValue: 0`; the five `TenantSettings`
string/enum columns and `FeedLastPulledAt` must be `nullable: true`.

If anything else appears, stop and report it. An unexpected `AlterColumn` means the model snapshot was
already out of sync, which is a pre-existing problem to raise rather than to bundle into this change.

- [ ] **Step 7: Verify build**

Run: `dotnet build`
Expected: success.

**Do not run `dotnet ef database update`.** Report to the developer that the app will not run until
they do. From here on, runtime verification steps are blocked on that.

*Commit point: `feat: add tenant branding, application origin, and feed pull timestamp`*

---

## Task 3: Brand colour validation and hover derivation (TDD)

The accent colour is written into a `style` attribute, so validating it is a security control, not a
nicety. This is the only tenant-supplied value that reaches CSS.

**Files:**
- Create: `tests/Ats.Tests/Branding/BrandColorTests.cs`
- Create: `src/Ats.Application/Branding/TenantBranding.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Ats.Tests/Branding/BrandColorTests.cs`:

```csharp
using Ats.Application.Branding;
using Xunit;

namespace Ats.Tests.Branding;

public class BrandColorTests
{
    [Theory]
    [InlineData("#0085CA")]
    [InlineData("#0085ca")]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    public void Normalize_accepts_six_digit_hex(string input)
    {
        Assert.Equal(input.ToUpperInvariant(), BrandColor.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0085CA")]          // missing hash
    [InlineData("#0085C")]          // five digits
    [InlineData("#0085CAA")]        // seven digits
    [InlineData("#00 85CA")]        // whitespace inside
    [InlineData("#GGGGGG")]         // not hex
    [InlineData("red")]             // named colour
    [InlineData("#0085CA;}")]       // CSS escape attempt
    [InlineData("var(--x)")]
    [InlineData("#0085CA\";background:url(x)")]
    public void Normalize_rejects_anything_else(string? input)
    {
        Assert.Null(BrandColor.Normalize(input));
    }

    [Fact]
    public void Normalize_trims_surrounding_whitespace()
    {
        Assert.Equal("#0085CA", BrandColor.Normalize("  #0085CA  "));
    }

    [Fact]
    public void Lighten_moves_each_channel_toward_white()
    {
        // 0.08 toward white reproduces the design system's #0085CA -> #128FCF hover
        // relationship to within one step per channel.
        Assert.Equal("#148FCE", BrandColor.Lighten("#0085CA", 0.08));
    }

    [Fact]
    public void Lighten_clamps_at_white()
    {
        Assert.Equal("#FFFFFF", BrandColor.Lighten("#FFFFFF", 0.5));
    }

    [Fact]
    public void Lighten_returns_null_for_an_invalid_colour()
    {
        Assert.Null(BrandColor.Lighten("nonsense", 0.08));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: compile error, `BrandColor` does not exist.

- [ ] **Step 3: Implement `BrandColor` and `TenantBranding`**

`src/Ats.Application/Branding/TenantBranding.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Ats.Domain.Enums;

namespace Ats.Application.Branding;

// The accent colour is emitted into a style attribute, so it is validated on the way in and again
// on the way out. Nothing else tenant-supplied reaches CSS.
public static partial class BrandColor
{
    public const string DefaultAccent = "#0085CA";       // NowOnline Sky Blue
    public const string DefaultAccentHover = "#128FCF";  // the design system's own hover token

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    // Returns the upper-cased colour, or null when the input is not a plain 6-digit hex colour.
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return HexPattern().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : null;
    }

    // Mixes the colour toward white by amount (0..1). Null when the input is invalid.
    public static string? Lighten(string? value, double amount)
    {
        var hex = Normalize(value);
        if (hex is null) return null;
        var t = Math.Clamp(amount, 0d, 1d);

        var r = Channel(hex, 1, t);
        var g = Channel(hex, 3, t);
        var b = Channel(hex, 5, t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int Channel(string hex, int offset, double t)
    {
        var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Math.Clamp((int)Math.Round(c + (255 - c) * t), 0, 255);
    }
}

public sealed record TenantBranding(
    string TenantName,
    string TenantSlug,
    string Accent,
    string AccentHover,
    SidebarTheme SidebarTheme,
    string? CareerHeroHeadline,
    string? CareerHeroHeadlineOutlined,
    string? CareerHeroIntro)
{
    public bool IsDarkSidebar => SidebarTheme == SidebarTheme.Dark;
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: 19 passed, 0 failed.

If `Lighten_moves_each_channel_toward_white` fails, print the actual value before changing anything:
the assertion encodes `0 -> 20 (0x14)`, `133 -> 143 (0x8F)`, `202 -> 206 (0xCE)`. Fix the
implementation, not the expectation.

*Commit point: `feat: add brand colour validation and hover derivation`*

---

## Task 4: Branding service

**Files:**
- Create: `src/Ats.Application/Branding/ITenantBrandingService.cs`
- Create: `src/Ats.Infrastructure/Branding/TenantBrandingService.cs`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Define the contract**

`src/Ats.Application/Branding/ITenantBrandingService.cs`:

```csharp
using Ats.Domain.Enums;

namespace Ats.Application.Branding;

public sealed record BrandingInput(
    string? AccentColor,
    SidebarTheme SidebarTheme,
    string? CareerHeroHeadline,
    string? CareerHeroHeadlineOutlined,
    string? CareerHeroIntro);

public interface ITenantBrandingService
{
    // Resolved branding for the current tenant, with NowOnline defaults substituted for nulls.
    // Cached for the lifetime of the request.
    Task<TenantBranding> GetAsync(CancellationToken ct = default);

    Task UpdateAsync(BrandingInput input, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement it**

`src/Ats.Infrastructure/Branding/TenantBrandingService.cs`. The tenant name and slug come from
`Tenants`, which is not an `ITenantEntity` and therefore unfiltered, so it is looked up by the current
tenant id explicitly. `TenantSettings` is tenant-filtered, so `FirstOrDefaultAsync` needs no predicate,
matching `IntegrationSettingsService`.

```csharp
using Ats.Application.Abstractions;
using Ats.Application.Branding;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Branding;

public sealed class TenantBrandingService : ITenantBrandingService
{
    private readonly AtsDbContext _db;
    private readonly ITenantContext _tenant;
    private TenantBranding? _cached;

    public TenantBrandingService(AtsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<TenantBranding> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var tenantId = _tenant.CurrentTenantId;
        if (tenantId is null) return _cached = Fallback();

        var tenant = await _db.Tenants
            .Where(t => t.Id == tenantId.Value)
            .Select(t => new { t.Name, t.Slug })
            .FirstOrDefaultAsync(ct);

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);

        var accent = BrandColor.Normalize(settings?.BrandAccentColor) ?? BrandColor.DefaultAccent;
        var hover = accent == BrandColor.DefaultAccent
            ? BrandColor.DefaultAccentHover
            : BrandColor.Lighten(accent, 0.08) ?? BrandColor.DefaultAccentHover;

        return _cached = new TenantBranding(
            TenantName: tenant?.Name ?? "ATS",
            TenantSlug: tenant?.Slug ?? string.Empty,
            Accent: accent,
            AccentHover: hover,
            SidebarTheme: settings?.BrandSidebarTheme ?? SidebarTheme.Dark,
            CareerHeroHeadline: settings?.CareerHeroHeadline,
            CareerHeroHeadlineOutlined: settings?.CareerHeroHeadlineOutlined,
            CareerHeroIntro: settings?.CareerHeroIntro);
    }

    public async Task UpdateAsync(BrandingInput input, CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct);

        // An invalid colour is stored as null, which resolves back to the default.
        settings.BrandAccentColor = BrandColor.Normalize(input.AccentColor);
        settings.BrandSidebarTheme = input.SidebarTheme;
        settings.CareerHeroHeadline = Trimmed(input.CareerHeroHeadline);
        settings.CareerHeroHeadlineOutlined = Trimmed(input.CareerHeroHeadlineOutlined);
        settings.CareerHeroIntro = Trimmed(input.CareerHeroIntro);

        await _db.SaveChangesAsync(ct);
        _cached = null;
    }

    private static string? Trimmed(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static TenantBranding Fallback() => new(
        "ATS", string.Empty, BrandColor.DefaultAccent, BrandColor.DefaultAccentHover,
        SidebarTheme.Dark, null, null, null);
}
```

- [ ] **Step 3: Register it**

In `src/Ats.Infrastructure/DependencyInjection.cs`, add the using and the registration next to the
other scoped services (scoped is what makes `_cached` a per-request cache):

```csharp
using Ats.Application.Branding;
using Ats.Infrastructure.Branding;
```

```csharp
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();
```

- [ ] **Step 4: Verify build and tests**

Run: `dotnet build` then `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: build succeeds, 19 tests pass.

*Commit point: `feat: add tenant branding service`*

---

## Task 5: Avatar palette (TDD)

**Files:**
- Create: `tests/Ats.Tests/Presentation/AvatarTests.cs`
- Create: `src/Ats.Application/Common/AvatarPalette.cs`

- [ ] **Step 1: Write the failing tests**

The expected initials are taken from the prototype, which uses the first letter of each of the first
two name tokens: "Fatima El Amrani" renders `FE`, not `FA` (prototype line 1088).

`tests/Ats.Tests/Presentation/AvatarTests.cs`:

```csharp
using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Presentation;

public class AvatarTests
{
    [Theory]
    [InlineData("Milan Verhoeven", "MV")]
    [InlineData("Fatima El Amrani", "FE")]   // first two tokens, not first + last
    [InlineData("Iris Draaijer", "ID")]
    [InlineData("sanne de vries", "SD")]
    [InlineData("Madonna", "MA")]            // single token: first two letters
    [InlineData("  Bram   Kooijman  ", "BK")]
    [InlineData("X", "X")]
    public void Initials_are_derived_from_the_name(string name, string expected)
    {
        Assert.Equal(expected, AvatarPalette.Initials(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Initials_fall_back_for_a_missing_name(string? name)
    {
        Assert.Equal("?", AvatarPalette.Initials(name));
    }

    [Fact]
    public void Colour_is_stable_for_the_same_name()
    {
        var a = AvatarPalette.For("Milan Verhoeven");
        var b = AvatarPalette.For("Milan Verhoeven");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Colour_ignores_case_and_surrounding_whitespace()
    {
        Assert.Equal(AvatarPalette.For("Milan Verhoeven"), AvatarPalette.For(" milan verhoeven "));
    }

    [Fact]
    public void Colour_comes_from_the_design_palette()
    {
        var pair = AvatarPalette.For("Milan Verhoeven");
        Assert.Contains(pair, AvatarPalette.Pairs);
    }

    [Fact]
    public void Different_names_spread_across_the_palette()
    {
        var names = new[]
        {
            "Milan Verhoeven", "Ravi Menon", "Anneke Wolters", "Joost Bakker",
            "Fatima El Amrani", "Iris Draaijer", "Bram Kooijman", "Sofia Marchetti",
            "Tim Hofstra", "Sanne de Vries"
        };
        var distinct = names.Select(AvatarPalette.For).Distinct().Count();
        Assert.True(distinct >= 3, $"expected the palette to spread, got {distinct} distinct pairs");
    }

    [Fact]
    public void Missing_name_uses_the_neutral_pair()
    {
        Assert.Equal(AvatarPalette.Neutral, AvatarPalette.For(null));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: compile error, `AvatarPalette` does not exist.

- [ ] **Step 3: Implement**

`src/Ats.Application/Common/AvatarPalette.cs`. `string.GetHashCode()` is randomized per process in
.NET and must not be used; the FNV-1a hash below is stable across runs and machines, which matters
because a person's avatar colour has to be the same on every page and every server.

```csharp
namespace Ats.Application.Common;

public sealed record AvatarColors(string Background, string Foreground);

// The five avatar colour pairs used throughout the redesign prototype.
public static class AvatarPalette
{
    public static readonly AvatarColors Neutral = new("#EFF0F2", "#5A6472");

    public static readonly IReadOnlyList<AvatarColors> Pairs = new[]
    {
        new AvatarColors("#EBF5FB", "#00679E"),   // sky
        new AvatarColors("#E8F6F0", "#00734D"),   // aqua
        new AvatarColors("#F0ECFB", "#5B3FBF"),   // violet
        new AvatarColors("#FDF3E7", "#A85400"),   // amber
        Neutral                                    // slate
    };

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return "?";

        if (tokens.Length == 1)
        {
            var single = tokens[0];
            return (single.Length >= 2 ? single[..2] : single).ToUpperInvariant();
        }

        return $"{tokens[0][0]}{tokens[1][0]}".ToUpperInvariant();
    }

    public static AvatarColors For(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Neutral;
        var index = (int)(Fnv1a(name.Trim().ToLowerInvariant()) % (uint)Pairs.Count);
        return Pairs[index];
    }

    // FNV-1a. Deterministic across processes, unlike string.GetHashCode().
    private static uint Fnv1a(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: 34 passed (19 from Task 3 plus 15 here: 7 + 3 theory cases and 5 facts).

If `Different_names_spread_across_the_palette` fails, do not weaken the assertion; report the actual
distinct count, because a hash that buckets ten Dutch names into two colours is a real defect.

*Commit point: `feat: add deterministic avatar palette`*

---

## Task 6: Relative time formatting (TDD)

Two shapes are needed: long ("4 minutes ago") for the feed-pull line, short ("3d") for board chips.

**Files:**
- Create: `tests/Ats.Tests/Presentation/RelativeTimeTests.cs`
- Create: `src/Ats.Application/Common/RelativeTime.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Ats.Application.Common;
using Xunit;

namespace Ats.Tests.Presentation;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(-30, "just now")]
    [InlineData(-60, "1 minute ago")]
    [InlineData(-240, "4 minutes ago")]
    [InlineData(-3600, "1 hour ago")]
    [InlineData(-7200, "2 hours ago")]
    [InlineData(-86400, "1 day ago")]
    [InlineData(-259200, "3 days ago")]
    public void Long_form_describes_the_age(int offsetSeconds, string expected)
    {
        var at = Now.AddSeconds(offsetSeconds);
        Assert.Equal(expected, RelativeTime.Long(at, Now));
    }

    [Fact]
    public void Long_form_handles_a_null_timestamp()
    {
        Assert.Equal("never", RelativeTime.Long(null, Now));
    }

    [Fact]
    public void Long_form_treats_a_future_timestamp_as_now()
    {
        Assert.Equal("just now", RelativeTime.Long(Now.AddMinutes(5), Now));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(-86400, "1d")]
    [InlineData(-259200, "3d")]
    [InlineData(-950400, "11d")]
    public void Short_form_is_a_compact_day_count(int offsetSeconds, string expected)
    {
        var at = Now.AddSeconds(offsetSeconds);
        Assert.Equal(expected, RelativeTime.ShortAge(at, Now));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-86400, 1)]
    [InlineData(-950400, 11)]
    public void Whole_days_counts_elapsed_days(int offsetSeconds, int expected)
    {
        Assert.Equal(expected, RelativeTime.WholeDays(Now.AddSeconds(offsetSeconds), Now));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: compile error, `RelativeTime` does not exist.

- [ ] **Step 3: Implement**

`src/Ats.Application/Common/RelativeTime.cs`:

```csharp
namespace Ats.Application.Common;

// `now` is a parameter rather than DateTimeOffset.UtcNow so this stays pure and testable.
public static class RelativeTime
{
    public static int WholeDays(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        return span < TimeSpan.Zero ? 0 : (int)span.TotalDays;
    }

    public static string Long(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null) return "never";

        var span = now - at.Value;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return Plural((int)span.TotalMinutes, "minute");
        if (span < TimeSpan.FromDays(1)) return Plural((int)span.TotalHours, "hour");
        return Plural((int)span.TotalDays, "day");
    }

    public static string ShortAge(DateTimeOffset at, DateTimeOffset now)
    {
        var days = WholeDays(at, now);
        return days == 0 ? "today" : $"{days}d";
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test tests/Ats.Tests/Ats.Tests.csproj`
Expected: 51 passed (34 so far plus 17 here: 8 + 4 + 3 theory cases and 2 facts).

*Commit point: `feat: add relative time formatting`*

---

## Task 7: Vendor the fonts and write the token layer

**Files:**
- Create: `src/Ats.Web/wwwroot/lib/nowonline-fonts/` (5 `.ttf` files)
- Create: `src/Ats.Web/wwwroot/css/ats-tokens.css`

- [ ] **Step 1: Copy the fonts out of the handoff bundle**

The empty `wwwroot/lib/nowonline-fonts` directory already exists. Copy all five variable TTFs from
`<bundle>/project/_ds/nowonline-design-system-3bae6b8b-982a-464f-9759-1335719d4108/fonts/` into it:
`Urbanist-VariableFont_wght.ttf`, `Urbanist-Italic-VariableFont_wght.ttf`,
`Lexend-VariableFont_wght.ttf`, `SometypeMono-VariableFont_wght.ttf`,
`SometypeMono-Italic-VariableFont_wght.ttf`.

Verify with `ls -la src/Ats.Web/wwwroot/lib/nowonline-fonts/`: five files, roughly 176 KB, 85 KB,
83 KB, 67 KB, 65 KB.

- [ ] **Step 2: Write `ats-tokens.css`**

This file is the whole contract between the design system and the app. Every value is copied verbatim
from the bundle's `colors_and_type.css`; nothing is invented. Two layers: the `--no-*` design system
tokens, then `--ats-*` semantic aliases and Bootstrap variable overrides that the rest of the app
consumes. Views never reference `--no-*` directly.

```css
/* NowOnline design system tokens for the ATS.
   Values are copied from the design system's colors_and_type.css. Do not hand-tune them here:
   change the design system and re-port. Views must consume the --ats-* aliases, never --no-*. */

:root {
  /* Brand accents */
  --no-oxford-blue: #0C2340;
  --no-sky-blue: #0085CA;
  --no-sky-blue-hover: #128FCF;
  --no-sky-blue-soft: #EBF5FB;
  --no-medium-aqua: #69CAA7;
  --no-medium-aqua-deep: #54A185;

  /* Neutrals */
  --no-maastricht-blue: #08182C;
  --no-charcoal: #394656;
  --no-roman-silver: #88909A;
  --no-platinum: #E1E3E6;
  --no-cultured: #F5F6F7;
  --no-white: #FFFFFF;

  /* Semantic */
  --no-danger: #EC003F;
  --no-warning: #E17100;
  --no-success: #009966;
  --no-info: #155DFC;

  /* Tints used by pills, chips and tinted rows in the prototype */
  --no-success-soft: #E8F6F0;  --no-success-ink: #00734D;
  --no-warning-soft: #FDF3E7;  --no-warning-ink: #A85400;
  --no-danger-soft:  #FDECF0;  --no-danger-ink:  #C1002F;
  --no-info-soft:    #EBF5FB;  --no-info-ink:    #00679E;
  --no-violet-soft:  #F0ECFB;  --no-violet-ink:  #5B3FBF;
  --no-slate-soft:   #EFF0F2;  --no-slate-ink:   #5A6472;
  --no-danger-row:   #FEF7F8;

  /* Stage ramp: Applied -> Screening -> Interview -> Offer -> Hired */
  --no-stage-1: #0C2340;
  --no-stage-2: #0085CA;
  --no-stage-3: #3BA7D8;
  --no-stage-4: #69CAA7;
  --no-stage-5: #54A185;
  --no-stage-empty: #EEF0F2;

  /* Shadows: Oxford-Blue tinted, never neutral black */
  --no-shadow-xs: 0 2px 6px 0 rgba(12, 35, 64, .03);
  --no-shadow-md: 0 10px 40px 0 rgba(12, 35, 64, .08);
  --no-shadow-lg: 0 10px 40px 0 rgba(12, 35, 64, .16);

  /* Radii */
  --no-radius-xs: 4px;
  --no-radius-sm: 8px;
  --no-radius-md: 12px;
  --no-radius-lg: 16px;
  --no-radius-xl: 24px;
  --no-radius-pill: 999px;

  /* Type families */
  --no-font-display: "Urbanist", system-ui, -apple-system, "Segoe UI", sans-serif;
  --no-font-body: "Lexend", system-ui, -apple-system, "Segoe UI", sans-serif;
  --no-font-mono: "Sometype Mono", ui-monospace, "SF Mono", Menlo, Consolas, monospace;

  --no-track-display: -0.01em;
  --no-track-eyebrow: 0.02em;

  /* ---- Semantic aliases. This is the app-facing API. ---- */
  --ats-accent: var(--no-sky-blue);
  --ats-accent-hover: var(--no-sky-blue-hover);
  --ats-accent-soft: var(--no-sky-blue-soft);

  --ats-bg: var(--no-cultured);
  --ats-surface: var(--no-white);
  --ats-surface-subtle: #FAFBFC;
  --ats-ink: var(--no-oxford-blue);
  --ats-ink-muted: var(--no-charcoal);
  --ats-ink-subtle: var(--no-roman-silver);
  --ats-ink-faint: #A6AEB8;
  --ats-border: var(--no-platinum);
  --ats-border-subtle: #F0F1F3;
  --ats-rule: #C9CDD3;

  /* Sidebar, overridden per tenant by the Branding view component */
  --ats-sidebar-bg: var(--no-oxford-blue);
  --ats-sidebar-fg: var(--no-white);
  --ats-sidebar-muted: #98A2AF;
  --ats-sidebar-label: #7A8493;
  --ats-sidebar-border: rgba(255, 255, 255, .10);
  --ats-sidebar-hover: rgba(255, 255, 255, .07);
  --ats-sidebar-chip: rgba(255, 255, 255, .05);
  --ats-sidebar-active: rgba(0, 133, 202, .22);

  --ats-sidebar-width: 252px;
  --ats-topbar-height: 60px;
}

/* ---- Bootstrap 5 variable overrides ----
   Bootstrap is kept for grid, forms, validation, collapse, modals and alerts. Overriding its
   variables here means .btn/.card/.table/.form-control inherit NowOnline instead of being
   fought with inline styles in views. */

:root {
  --bs-body-font-family: var(--no-font-body);
  --bs-body-font-size: .9375rem;
  --bs-body-font-weight: 300;
  --bs-body-line-height: 1.56;
  --bs-body-color: var(--ats-ink);
  --bs-body-bg: var(--ats-bg);
  --bs-border-color: var(--ats-border);
  --bs-border-radius: var(--no-radius-md);
  --bs-border-radius-sm: var(--no-radius-sm);
  --bs-border-radius-lg: var(--no-radius-lg);
  --bs-link-color: var(--ats-accent);
  --bs-link-hover-color: var(--ats-accent-hover);
  --bs-primary: var(--ats-accent);
  --bs-emphasis-color: var(--ats-ink);
  --bs-secondary-color: var(--ats-ink-subtle);
  --bs-heading-color: var(--ats-ink);
}

/* Buttons: pill, Urbanist, no scale on press (design system interaction rules). */
.btn {
  --bs-btn-font-family: var(--no-font-display);
  --bs-btn-font-weight: 600;
  --bs-btn-font-size: .875rem;
  --bs-btn-border-radius: var(--no-radius-pill);
  --bs-btn-padding-x: 1.125rem;
  --bs-btn-padding-y: .5625rem;
}

.btn-primary {
  --bs-btn-font-weight: 700;
  --bs-btn-color: #fff;
  --bs-btn-bg: var(--ats-accent);
  --bs-btn-border-color: var(--ats-accent);
  --bs-btn-hover-color: #fff;
  --bs-btn-hover-bg: var(--ats-accent-hover);
  --bs-btn-hover-border-color: var(--ats-accent-hover);
  --bs-btn-active-bg: var(--ats-accent-hover);
  --bs-btn-active-border-color: var(--ats-accent-hover);
  --bs-btn-disabled-bg: var(--ats-accent);
  --bs-btn-disabled-border-color: var(--ats-accent);
}

/* Ghost buttons invert to Oxford Blue on hover, per the design system. */
.btn-outline-secondary {
  --bs-btn-color: var(--ats-ink);
  --bs-btn-border-color: var(--ats-border);
  --bs-btn-bg: var(--ats-surface);
  --bs-btn-hover-color: #fff;
  --bs-btn-hover-bg: var(--no-oxford-blue);
  --bs-btn-hover-border-color: var(--no-oxford-blue);
  --bs-btn-active-color: #fff;
  --bs-btn-active-bg: var(--no-oxford-blue);
  --bs-btn-active-border-color: var(--no-oxford-blue);
}

.btn-outline-danger {
  --bs-btn-color: var(--no-danger-ink);
  --bs-btn-border-color: #F0DDE2;
  --bs-btn-bg: var(--ats-surface);
  --bs-btn-hover-color: var(--no-danger-ink);
  --bs-btn-hover-bg: var(--no-danger-soft);
  --bs-btn-hover-border-color: #F0DDE2;
}

.card {
  --bs-card-bg: var(--ats-surface);
  --bs-card-border-color: var(--ats-border);
  --bs-card-border-radius: var(--no-radius-lg);
  --bs-card-inner-border-radius: var(--no-radius-lg);
  --bs-card-spacer-x: 1.375rem;
  --bs-card-spacer-y: 1.375rem;
  box-shadow: var(--no-shadow-xs);
}

.form-control, .form-select {
  --bs-border-radius: var(--no-radius-sm);
  font-weight: 300;
}
.form-control:focus, .form-select:focus {
  border-color: var(--ats-accent);
  box-shadow: 0 0 0 .2rem color-mix(in srgb, var(--ats-accent) 18%, transparent);
}
.form-label { font-size: .8125rem; color: var(--ats-ink-muted); margin-bottom: .375rem; }

.table {
  --bs-table-bg: var(--ats-surface);
  --bs-table-border-color: var(--ats-border-subtle);
  --bs-table-hover-bg: var(--ats-surface-subtle);
}
.table > thead {
  font-family: var(--no-font-mono);
  font-weight: 700;
  font-size: .65625rem;
  letter-spacing: var(--no-track-eyebrow);
  color: var(--ats-ink-subtle);
  text-transform: none;
}
.table > thead th { background: var(--ats-surface-subtle); border-bottom-color: var(--ats-border); font-weight: 700; }

.alert { --bs-alert-border-radius: var(--no-radius-md); border-width: 1px; }

/* Focus: 2px accent ring at 2px offset, never the browser default. */
:focus-visible { outline: 2px solid var(--ats-accent); outline-offset: 2px; }
```

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success. CSS is not compiled, so this only confirms nothing else broke. Visual verification
happens in Task 15 once the layout consumes the file.

*Commit point: `feat: vendor NowOnline fonts and add design token layer`*

---

## Task 8: Base and component stylesheets

**Files:**
- Create: `src/Ats.Web/wwwroot/css/ats-base.css`
- Create: `src/Ats.Web/wwwroot/css/ats-components.css`

- [ ] **Step 1: Write `ats-base.css`**

```css
/* Fonts, typography and base elements. Depends on ats-tokens.css. */

@font-face {
  font-family: "Urbanist";
  src: url("../lib/nowonline-fonts/Urbanist-VariableFont_wght.ttf") format("truetype-variations");
  font-weight: 100 900; font-style: normal; font-display: swap;
}
@font-face {
  font-family: "Urbanist";
  src: url("../lib/nowonline-fonts/Urbanist-Italic-VariableFont_wght.ttf") format("truetype-variations");
  font-weight: 100 900; font-style: italic; font-display: swap;
}
@font-face {
  font-family: "Lexend";
  src: url("../lib/nowonline-fonts/Lexend-VariableFont_wght.ttf") format("truetype-variations");
  font-weight: 100 900; font-style: normal; font-display: swap;
}
@font-face {
  font-family: "Sometype Mono";
  src: url("../lib/nowonline-fonts/SometypeMono-VariableFont_wght.ttf") format("truetype-variations");
  font-weight: 400 700; font-style: normal; font-display: swap;
}
@font-face {
  font-family: "Sometype Mono";
  src: url("../lib/nowonline-fonts/SometypeMono-Italic-VariableFont_wght.ttf") format("truetype-variations");
  font-weight: 400 700; font-style: italic; font-display: swap;
}

html, body { height: 100%; }
body {
  background: var(--ats-bg);
  color: var(--ats-ink);
  font-family: var(--no-font-body);
  font-weight: 300;
  -webkit-font-smoothing: antialiased;
  text-rendering: optimizeLegibility;
}

h1, h2, h3, h4, h5, h6 {
  font-family: var(--no-font-display);
  color: var(--ats-ink);
  letter-spacing: var(--no-track-display);
}
h1 { font-size: 2.125rem; line-height: 1.15; font-weight: 800; }
h2 { font-size: 1.25rem; line-height: 1.25; font-weight: 800; }
h3 { font-size: 1.1875rem; font-weight: 800; }
h4, h5, h6 { font-weight: 700; }

code, kbd, samp, pre { font-family: var(--no-font-mono); font-size: .75rem; }
code { color: var(--no-slate-ink); }

a { color: var(--ats-accent); text-decoration: none; }
a:hover { color: var(--no-oxford-blue); }

/* Material Symbols Outlined, self-hosted. Replaces Bootstrap Icons.
   Usage: <span class="ms">work_outline</span> */
.ms {
  font-family: "Material Symbols Outlined";
  font-weight: normal; font-style: normal;
  font-size: 1.1875rem; line-height: 1;
  letter-spacing: normal; text-transform: none;
  display: inline-block; white-space: nowrap; direction: ltr;
  -webkit-font-feature-settings: "liga"; -webkit-font-smoothing: antialiased;
  font-variation-settings: "FILL" 0, "wght" 400, "GRAD" 0, "opsz" 24;
  vertical-align: middle;
  user-select: none;
}
.ms-sm { font-size: 1rem; }
.ms-lg { font-size: 1.375rem; }

/* Eyebrow / kicker: Sometype Mono bold, sentence case, ends in a colon.
   The single most distinctive mechanism in the brand. */
.ats-eyebrow {
  font-family: var(--no-font-mono);
  font-weight: 700;
  font-size: .75rem;
  line-height: 1;
  letter-spacing: var(--no-track-eyebrow);
  color: var(--ats-ink-subtle);
  text-transform: none;
}
.ats-mono { font-family: var(--no-font-mono); font-weight: 700; }
.ats-muted { color: var(--ats-ink-subtle); }
.ats-faint { color: var(--ats-ink-faint); }

/* Scrollbars inside scroll regions, matching the prototype. */
.ats-scroll::-webkit-scrollbar { width: 10px; height: 10px; }
.ats-scroll::-webkit-scrollbar-thumb {
  background: #DFE2E6; border-radius: var(--no-radius-pill); border: 3px solid var(--ats-bg);
}
```

- [ ] **Step 2: Write `ats-components.css`**

Every class below is used by a partial created in Task 11 or a screen in a later phase. Do not add
classes speculatively; a class with no consumer is dead code.

Required class inventory, with the visual contract for each. Sizes and colours come from the
prototype lines cited:

| Class | Contract | Prototype |
|---|---|---|
| `.ats-card` | white, 1px `--ats-border`, radius `--no-radius-lg`, padding 1.375rem, `--no-shadow-xs` | L142 |
| `.ats-card-flush` | same but `padding:0; overflow:hidden`, for table shells | L298 |
| `.ats-card-dark` | `--no-oxford-blue` bg, white text, radius `--no-radius-lg`, padding 1.375rem, no border | L230 |
| `.ats-card-hover` | transition box-shadow/transform .15s; on hover `--no-shadow-lg` and `translateY(-1px)` | L41-42 |
| `.ats-stat` | flex column, gap .625rem | L142 |
| `.ats-stat-value` | Urbanist 800, 2.5rem, line-height 1, tracking `--no-track-display` | L144 |
| `.ats-stat-unit` | 1.125rem, 700, `--ats-ink-subtle`, inline inside the value | L154 |
| `.ats-stat-delta` | inline-flex, gap .3125rem, .78125rem, weight 300 | L145 |
| `.ats-stat-strip` | flex, gap 2.125rem; children are label-over-value pairs | L420-425 |
| `.ats-pill` | inline-flex, gap .375rem, radius pill, padding .25rem .6875rem, .75rem | L308 |
| `.ats-pill-dot` | 6px circle, `currentColor` unless overridden | L308 |
| `.ats-pill--success/-warning/-danger/-info/-neutral` | soft bg + ink pairs from the tint tokens | L308, 338, 368 |
| `.ats-chip` | inline-flex, gap .25rem, radius pill, padding .1875rem .5625rem, .6875rem | L442 |
| `.ats-chip--neutral/-info/-warning/-danger` | tint pairs | L442-443 |
| `.ats-avatar` | circle, flex-centre, Urbanist 800; size via `--ats-avatar-size` default 2rem | L438 |
| `.ats-avatar-stack` | flex; children after the first get `margin-left:-8px` and a 2px white ring | L313 |
| `.ats-pipebar` | flex, gap 3px, height 7px; children are flex-weighted pill segments | L310 |
| `.ats-pipebar-row` | label (5.75rem) + 26px track + right-aligned count, gap .875rem | L175-179 |
| `.ats-progress-dots` | flex, gap 2px; 6px circles, filled `--no-medium-aqua`, empty `#DFE2E6` | L444 |
| `.ats-filter-group` | white, 1px border, radius pill, padding 3px, gap 2px; active child Oxford Blue on white | L287-292 |
| `.ats-toolbar` | flex, gap .625rem, wrap, align centre | L282 |
| `.ats-search` | flex, gap .5rem, `--ats-bg` fill, 1px border, radius pill, padding .4375rem .875rem; borderless transparent input | L115-119 |
| `.ats-kbd` | mono .625rem 700, 1px border, radius 5px, padding 1px 5px, white bg | L118 |
| `.ats-table-grid` | CSS grid row; column template supplied per screen via inline `grid-template-columns` | L299 |
| `.ats-trow` | grid row, 1px bottom `--ats-border-subtle`, hover `--ats-surface-subtle`, cursor pointer | L40, 303 |
| `.ats-trow--danger` | as above with `--no-danger-row` background | L848 |
| `.ats-thead` | grid row, `--ats-surface-subtle`, mono 700 .65625rem `--ats-ink-subtle`, 1px bottom `--ats-border` | L299 |
| `.ats-board` | flex, gap .875rem, `overflow-x:auto`, `align-items:flex-start` | L427 |
| `.ats-board-col` | 268px fixed, `#EFF1F3`, radius 14px, padding .75rem, flex column gap .625rem | L429 |
| `.ats-board-col--hired` | `#EAF4EF` | L553 |
| `.ats-board-col--rejected` | `#F5EEF0` | L563 |
| `.ats-board-col-head` | flex, gap .5rem, padding 2px 4px; 8px stage dot, Urbanist 700 .84375rem, mono count | L430-434 |
| `.ats-board-card` | white, 1px `#E4E7EA`, radius `--no-radius-md`, padding .8125rem, flex column gap .625rem, cursor pointer; composes `.ats-card-hover` | L436 |
| `.ats-board-drop` | 1px dashed `#C4D8CE`, radius `--no-radius-md`, padding 1.25rem .75rem, centred .75rem `#6C8579` | L560 |
| `.ats-drawer-backdrop` | fixed inset 0, `rgba(12,35,64,.32)`, flex end, z-index 1045 | L975 |
| `.ats-drawer` | 520px, full height, white, `-10px 0 40px rgba(12,35,64,.16)`, flex column, `overflow:auto` | L976 |
| `.ats-drawer-in` | `slidein .22s cubic-bezier(.2,.8,.2,1)` from `translateX(24px)`/opacity 0 | L43-44 |
| `.ats-drawer-section` | padding 1.375rem 1.625rem, 1px bottom `--ats-border-subtle`, flex column gap .875rem | L994 |
| `.ats-timeline` | flex column; each item is a 12px rail (10px dot + 1.5px `#E7E9EC` line) plus content | L1024-1035 |
| `.ats-timeline-dot--current` | dot filled `--ats-accent` | L1025 |
| `.ats-empty` | centred, padding 2.5rem 1rem, `--ats-ink-subtle`; 2rem icon, then headline, then body | new |
| `.ats-toggle` | 40x23 pill track, 17px white knob right when on, `--ats-accent` when on, `--ats-rule` when off | L805 |
| `.ats-pager` | flex gap .375rem; 34px circular buttons, current filled Oxford Blue | L382-386 |
| `.ats-tabs` | flex gap 1.625rem, 1px bottom `--ats-border`; active child 2px `--ats-accent` bottom, `margin-bottom:-1px` | L413-417 |
| `.ats-browser-frame` | 1px border, radius `--no-radius-lg`, `overflow:hidden`, `--no-shadow-md`; 40px `--ats-bg` chrome with three 10px dots and a pill URL | L920-924 |

Write the file with the classes in that order, grouped by the comment headings: surfaces, stats,
pills and chips, avatars, pipeline, controls, tables, board, drawer, timeline, misc.

- [ ] **Step 3: Verify no class is orphaned**

Run: `dotnet build`
Expected: success.

Then confirm the inventory matches the file:

```bash
grep -oE "^\.ats-[a-z0-9-]+" src/Ats.Web/wwwroot/css/ats-components.css | sort -u | wc -l
```

Expected: at least 40 distinct classes. Any class in the file but not in the table above should be
removed unless a partial in Task 11 uses it.

*Commit point: `feat: add base and component stylesheets`*

---

## Task 9: Shell stylesheet

**Files:**
- Create: `src/Ats.Web/wwwroot/css/ats-shell.css`

- [ ] **Step 1: Write the file**

The shell is a fixed-height flex row: sidebar, then a column of topbar plus scrolling main. Only
`main` scrolls, matching the prototype (L50, L124).

```css
/* Back-office shell: sidebar, topbar, content, drawer host. Depends on ats-tokens.css. */

.ats-shell {
  height: 100vh;
  display: flex;
  overflow: hidden;
  background: var(--ats-bg);
}

/* ---- Sidebar ---- */
.ats-sidebar {
  width: var(--ats-sidebar-width);
  flex: 0 0 var(--ats-sidebar-width);
  background: var(--ats-sidebar-bg);
  color: var(--ats-sidebar-fg);
  border-right: 1px solid var(--ats-sidebar-border);
  display: flex;
  flex-direction: column;
  padding: 1.25rem .75rem 1rem;
  gap: 1.25rem;
}

.ats-brand { display: flex; align-items: center; gap: .625rem; padding: 0 .5rem; }
.ats-brand-mark {
  width: 30px; height: 30px; flex: 0 0 30px;
  border-radius: var(--no-radius-sm);
  background: var(--ats-accent); color: #fff;
  display: flex; align-items: center; justify-content: center;
  font-family: var(--no-font-display); font-weight: 800; font-size: .875rem;
}
.ats-brand-name {
  font-family: var(--no-font-display); font-weight: 800; font-size: 1rem;
  letter-spacing: var(--no-track-display); color: var(--ats-sidebar-fg);
}
.ats-brand-sub {
  font-family: var(--no-font-mono); font-weight: 700; font-size: .625rem;
  color: var(--ats-sidebar-muted);
}

.ats-tenant-chip {
  display: flex; align-items: center; gap: .625rem; width: 100%;
  padding: .625rem; border-radius: var(--no-radius-md);
  border: 1px solid var(--ats-sidebar-border);
  background: var(--ats-sidebar-chip);
  text-align: left; color: inherit;
}
.ats-tenant-chip-mark {
  width: 28px; height: 28px; flex: 0 0 28px;
  border-radius: var(--no-radius-sm);
  background: var(--no-medium-aqua); color: var(--no-oxford-blue);
  display: flex; align-items: center; justify-content: center;
  font-family: var(--no-font-display); font-weight: 800; font-size: .6875rem;
}
.ats-tenant-chip-name {
  font-size: .8125rem; color: var(--ats-sidebar-fg);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}

.ats-nav { display: flex; flex-direction: column; gap: 1.375rem; flex: 1; overflow: auto; }
.ats-nav-group { display: flex; flex-direction: column; gap: 2px; }
.ats-nav-label {
  font-family: var(--no-font-mono); font-weight: 700; font-size: .65625rem;
  letter-spacing: var(--no-track-eyebrow);
  color: var(--ats-sidebar-label); padding: 0 .625rem .375rem;
}

.ats-nav a {
  position: relative;
  display: flex; align-items: center; gap: .625rem;
  padding: .5625rem .625rem; border-radius: var(--no-radius-md);
  color: var(--ats-sidebar-muted); font-size: .84375rem; font-weight: 300;
  transition: background .15s ease, color .15s ease;
}
.ats-nav a:hover { background: var(--ats-sidebar-hover); color: var(--ats-sidebar-fg); }
.ats-nav a.active {
  background: var(--ats-sidebar-active); color: var(--ats-sidebar-fg); font-weight: 500;
}
/* The 3px accent rail on the active item. The sidebar's own padding is 12px, so the rail
   sits at -12px to hug the panel edge. */
.ats-nav a.active::before {
  content: ""; position: absolute; left: -.75rem; top: .5rem; bottom: .5rem;
  width: 3px; border-radius: 0 3px 3px 0; background: var(--ats-accent);
}
.ats-nav-count {
  margin-left: auto;
  font-family: var(--no-font-mono); font-weight: 700; font-size: .625rem;
  color: var(--ats-sidebar-label);
}
.ats-nav-alert {
  margin-left: auto; width: 7px; height: 7px; border-radius: var(--no-radius-pill);
  background: var(--no-danger);
}

.ats-sidebar-user {
  border-top: 1px solid var(--ats-sidebar-border); padding-top: .75rem;
  display: flex; align-items: center; gap: .625rem;
}
.ats-sidebar-user-name { font-size: .8125rem; color: var(--ats-sidebar-fg); }
.ats-sidebar-user-role {
  font-family: var(--no-font-mono); font-weight: 700; font-size: .625rem;
  color: var(--ats-sidebar-muted);
}

/* ---- Main column ---- */
.ats-main { flex: 1; display: flex; flex-direction: column; min-width: 0; }

.ats-topbar {
  height: var(--ats-topbar-height); flex: 0 0 var(--ats-topbar-height);
  background: var(--ats-surface); border-bottom: 1px solid var(--ats-border);
  display: flex; align-items: center; gap: 1rem; padding: 0 1.75rem;
}
.ats-crumbs {
  display: flex; align-items: center; gap: .5rem;
  font-size: .8125rem; font-weight: 300; color: var(--ats-ink-subtle);
}
.ats-crumbs-sep { color: var(--ats-rule); }
.ats-crumbs-leaf { color: var(--ats-ink); }

.ats-topbar-search { margin-left: auto; width: 20rem; position: relative; }
.ats-topbar-results {
  position: absolute; top: calc(100% + .5rem); left: 0; right: 0; z-index: 1040;
  background: var(--ats-surface); border: 1px solid var(--ats-border);
  border-radius: var(--no-radius-md); box-shadow: var(--no-shadow-md);
  max-height: 24rem; overflow: auto;
}
.ats-topbar-results:empty { display: none; }

.ats-icon-btn {
  width: 36px; height: 36px; flex: 0 0 36px;
  border-radius: var(--no-radius-pill); border: 1px solid var(--ats-border);
  background: var(--ats-surface); color: var(--ats-ink-muted);
  display: flex; align-items: center; justify-content: center;
  position: relative;
}
.ats-icon-btn:hover { background: var(--ats-bg); }
.ats-icon-btn-dot {
  position: absolute; top: 7px; right: 8px;
  width: 7px; height: 7px; border-radius: var(--no-radius-pill);
  background: var(--no-danger); border: 1.5px solid var(--ats-surface);
}

.ats-content { flex: 1; overflow: auto; padding: 1.75rem 1.75rem 3.5rem; }
.ats-content > * { max-width: 1400px; }

.ats-pagehead {
  display: flex; align-items: flex-end; justify-content: space-between;
  gap: 1.5rem; flex-wrap: wrap; margin-bottom: 1.25rem;
}
.ats-pagehead-text { display: flex; flex-direction: column; gap: .5rem; }
.ats-pagehead-actions { display: flex; gap: .5rem; align-items: center; }

/* Drawer host: empty until htmx swaps a drawer in. */
#ats-drawer-host:empty { display: none; }

/* ---- Auth shell ---- */
.ats-auth {
  min-height: 100vh; display: flex; align-items: center; justify-content: center;
  padding: 1rem;
  background: linear-gradient(160deg, var(--no-oxford-blue), var(--no-maastricht-blue));
}
.ats-auth-card { width: 100%; max-width: 26rem; }
.ats-auth-brand {
  display: flex; align-items: center; justify-content: center; gap: .625rem;
  margin-bottom: 1.25rem; color: #fff;
}

@media (max-width: 991.98px) {
  .ats-shell { height: auto; overflow: visible; display: block; }
  .ats-sidebar { width: 100%; flex: none; flex-direction: row; flex-wrap: wrap; height: auto; }
  .ats-nav { flex-direction: row; flex-wrap: wrap; gap: .5rem; overflow: visible; }
  .ats-nav-label { display: none; }
  .ats-nav a.active::before { display: none; }
  .ats-content { padding: 1rem; }
  .ats-topbar-search { width: 100%; order: 3; }
  .ats-drawer { width: 100%; }
}
```

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: add shell stylesheet`*

---

## Task 10: Swap Bootstrap Icons for Material Symbols

**Files:**
- Modify: `libman.json`
- Create: `src/Ats.Web/wwwroot/lib/material-symbols/` (via restore)
- Delete: `src/Ats.Web/wwwroot/lib/bootstrap-icons/`

- [ ] **Step 1: Point LibMan at Material Symbols**

Replace the `bootstrap-icons` entry in `libman.json` with `material-symbols`. Keep `htmx.org` and
`sortablejs` exactly as they are.

```json
{
  "version": "1.0",
  "defaultProvider": "unpkg",
  "libraries": [
    {
      "library": "material-symbols@0.31.8",
      "destination": "src/Ats.Web/wwwroot/lib/material-symbols",
      "files": [
        "index.css",
        "material-symbols-outlined.woff2"
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

- [ ] **Step 2: Restore**

Run: `libman restore`
Expected: the two Material Symbols files land in `src/Ats.Web/wwwroot/lib/material-symbols/`.

If `libman` is not on PATH, install it with `dotnet tool install -g Microsoft.Web.LibraryManager.Cli`
and retry. If the package's file list differs from the two names above, run
`libman cache list material-symbols` or inspect
`https://unpkg.com/browse/material-symbols/` and correct the `files` array. The requirement is one
CSS file plus the **outlined** woff2; do not pull the rounded or sharp variants.

- [ ] **Step 3: Point the `@font-face` at the restored file**

`material-symbols/index.css` declares the family with its own relative `url(...)`. Reference that
file from the layouts (Task 15) rather than duplicating the `@font-face`. The `.ms` class in
`ats-base.css` already declares `font-family: "Material Symbols Outlined"`, which is the family name
that CSS defines.

Confirm the family name matches:

```bash
grep -o 'font-family:[^;]*' src/Ats.Web/wwwroot/lib/material-symbols/index.css | head -3
```

Expected: `Material Symbols Outlined`. If it differs, update `.ms` in `ats-base.css` to match, not
the vendored file.

- [ ] **Step 4: Delete the Bootstrap Icons files**

Remove `src/Ats.Web/wwwroot/lib/bootstrap-icons/` and its three tracked files. Do this only after
Task 19 has removed the last `bi-*` reference, or the app renders empty boxes in between. If working
strictly in order, defer the deletion to Task 19 Step 4 and leave a note here.

- [ ] **Step 5: Verify**

Run: `dotnet build`
Expected: success.

*Commit point: `chore: replace Bootstrap Icons with self-hosted Material Symbols`*

---

## Task 11: Shared presentation partials

Small view models keep these partials strongly typed instead of passing `dynamic` or `ViewData`.

**Files:**
- Create: `src/Ats.Web/Models/Shared/PresentationModels.cs`
- Create: `src/Ats.Web/Views/Shared/Partials/_Avatar.cshtml`
- Create: `src/Ats.Web/Views/Shared/Partials/_StatusPill.cshtml`
- Create: `src/Ats.Web/Views/Shared/Partials/_SourceChip.cshtml`
- Create: `src/Ats.Web/Views/Shared/Partials/_StatTile.cshtml`
- Create: `src/Ats.Web/Views/Shared/Partials/_PipelineBar.cshtml`
- Create: `src/Ats.Web/Views/Shared/Partials/_EmptyState.cshtml`
- Create: `src/Ats.Web/Views/Shared/Partials/_Timeline.cshtml`

- [ ] **Step 1: Define the view models**

`src/Ats.Web/Models/Shared/PresentationModels.cs`:

```csharp
using Ats.Domain.Enums;

namespace Ats.Web.Models.Shared;

public enum PillTone { Neutral, Success, Warning, Danger, Info }

public sealed record AvatarModel(string? Name, double SizeRem = 2.0, bool Ring = false);

public sealed record StatusPillModel(string Label, PillTone Tone, bool ShowDot = true);

public sealed record SourceChipModel(ApplicationOrigin Origin);

public sealed record StatTileModel(
    string Eyebrow,
    string Value,
    string? Unit = null,
    string? DeltaText = null,
    string? DeltaIcon = null,
    PillTone DeltaTone = PillTone.Neutral);

public sealed record PipelineSegment(string Label, int Count);

public sealed record PipelineBarModel(IReadOnlyList<PipelineSegment> Segments, bool ShowLabels = false);

public sealed record EmptyStateModel(string Icon, string Headline, string? Body = null);

public sealed record TimelineItem(string Title, string? Subtitle, bool IsCurrent = false);

public sealed record TimelineModel(IReadOnlyList<TimelineItem> Items);
```

- [ ] **Step 2: Write `_Avatar.cshtml`**

```razor
@using Ats.Application.Common
@model Ats.Web.Models.Shared.AvatarModel
@{
    var colors = AvatarPalette.For(Model.Name);
    var initials = AvatarPalette.Initials(Model.Name);
    var ring = Model.Ring ? "box-shadow:0 0 0 2px var(--ats-surface);" : "";
}
<span class="ats-avatar" title="@Model.Name"
      style="--ats-avatar-size:@(Model.SizeRem.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))rem;background:@colors.Background;color:@colors.Foreground;@ring">@initials</span>
```

The `.ats-avatar` class must size itself from `--ats-avatar-size`, so add to `ats-components.css`:

```css
.ats-avatar {
  --ats-avatar-size: 2rem;
  width: var(--ats-avatar-size); height: var(--ats-avatar-size);
  flex: 0 0 var(--ats-avatar-size);
  border-radius: var(--no-radius-pill);
  display: inline-flex; align-items: center; justify-content: center;
  font-family: var(--no-font-display); font-weight: 800;
  font-size: calc(var(--ats-avatar-size) * .36);
  overflow: hidden;
}
```

- [ ] **Step 3: Write `_StatusPill.cshtml`**

```razor
@model Ats.Web.Models.Shared.StatusPillModel
@{
    var tone = Model.Tone switch
    {
        Ats.Web.Models.Shared.PillTone.Success => "ats-pill--success",
        Ats.Web.Models.Shared.PillTone.Warning => "ats-pill--warning",
        Ats.Web.Models.Shared.PillTone.Danger => "ats-pill--danger",
        Ats.Web.Models.Shared.PillTone.Info => "ats-pill--info",
        _ => "ats-pill--neutral"
    };
}
<span class="ats-pill @tone">
    @if (Model.ShowDot)
    {
        <span class="ats-pill-dot"></span>
    }
    @Model.Label
</span>
```

- [ ] **Step 4: Write `_SourceChip.cshtml`**

`Unknown` renders as a neutral "Not recorded" chip. It must not claim a source the data does not have:
every application that predates the `Origin` column is `Unknown`.

```razor
@using Ats.Domain.Enums
@model Ats.Web.Models.Shared.SourceChipModel
@{
    var (icon, label, cls) = Model.Origin switch
    {
        ApplicationOrigin.CareerSite => ("public", "Career site", "ats-chip--neutral"),
        ApplicationOrigin.Referral => ("share", "Referral", "ats-chip--info"),
        ApplicationOrigin.Manual => ("person_add", "Manual", "ats-chip--neutral"),
        _ => ("help_outline", "Not recorded", "ats-chip--neutral")
    };
}
<span class="ats-chip @cls"><span class="ms ms-sm">@icon</span>@label</span>
```

- [ ] **Step 5: Write `_StatTile.cshtml`**

```razor
@model Ats.Web.Models.Shared.StatTileModel
@{
    var deltaColor = Model.DeltaTone switch
    {
        Ats.Web.Models.Shared.PillTone.Success => "var(--no-success-ink)",
        Ats.Web.Models.Shared.PillTone.Warning => "var(--no-warning-ink)",
        Ats.Web.Models.Shared.PillTone.Danger => "var(--no-danger-ink)",
        _ => "var(--ats-ink-muted)"
    };
}
<div class="ats-card ats-stat">
    <span class="ats-eyebrow">@Model.Eyebrow</span>
    <span class="ats-stat-value">
        @Model.Value@if (Model.Unit is not null) { <span class="ats-stat-unit">@Model.Unit</span> }
    </span>
    @if (Model.DeltaText is not null)
    {
        <span class="ats-stat-delta" style="color:@deltaColor">
            @if (Model.DeltaIcon is not null) { <span class="ms ms-sm">@Model.DeltaIcon</span> }
            @Model.DeltaText
        </span>
    }
</div>
```

- [ ] **Step 6: Write `_PipelineBar.cshtml`**

Segments map onto the five-step stage ramp by position, cycling if a pipeline has more than five
stages. A segment with a zero count still needs a visible sliver, hence `Math.Max(1, ...)`.

```razor
@model Ats.Web.Models.Shared.PipelineBarModel
@{
    var ramp = new[] { "var(--no-stage-1)", "var(--no-stage-2)", "var(--no-stage-3)", "var(--no-stage-4)", "var(--no-stage-5)" };
    var total = Model.Segments.Sum(s => s.Count);
}
@if (total == 0)
{
    <span class="ats-pipebar"><span style="flex:1;background:var(--no-stage-empty)"></span></span>
}
else
{
    <span class="ats-pipebar">
        @for (var i = 0; i < Model.Segments.Count; i++)
        {
            var seg = Model.Segments[i];
            if (seg.Count == 0) { continue; }
            <span style="flex:@Math.Max(1, seg.Count);background:@ramp[i % ramp.Length]"
                  title="@seg.Count @seg.Label"></span>
        }
    </span>
}
@if (Model.ShowLabels)
{
    <span class="ats-muted" style="font-size:.71875rem;font-weight:300">
        @string.Join(" · ", Model.Segments.Where(s => s.Count > 0).Select(s => $"{s.Count} {s.Label.ToLowerInvariant()}"))
    </span>
}
```

- [ ] **Step 7: Write `_EmptyState.cshtml` and `_Timeline.cshtml`**

`_EmptyState.cshtml`:

```razor
@model Ats.Web.Models.Shared.EmptyStateModel
<div class="ats-empty">
    <span class="ms ms-lg ats-faint">@Model.Icon</span>
    <div class="ats-empty-headline">@Model.Headline</div>
    @if (Model.Body is not null)
    {
        <div class="ats-empty-body">@Model.Body</div>
    }
</div>
```

`_Timeline.cshtml`:

```razor
@model Ats.Web.Models.Shared.TimelineModel
<div class="ats-timeline">
    @for (var i = 0; i < Model.Items.Count; i++)
    {
        var item = Model.Items[i];
        var last = i == Model.Items.Count - 1;
        <div class="ats-timeline-item">
            <span class="ats-timeline-rail">
                <span class="ats-timeline-dot @(item.IsCurrent ? "ats-timeline-dot--current" : "")"></span>
                @if (!last) { <span class="ats-timeline-line"></span> }
            </span>
            <span class="ats-timeline-body @(last ? "" : "ats-timeline-body--spaced")">
                <span class="ats-timeline-title">@item.Title</span>
                @if (item.Subtitle is not null) { <span class="ats-timeline-sub">@item.Subtitle</span> }
            </span>
        </div>
    }
</div>
```

Add the matching classes to `ats-components.css`: `.ats-timeline-item` (flex, gap .875rem),
`.ats-timeline-rail` (flex column, align centre, `flex:0 0 12px`), `.ats-timeline-dot`
(10px circle, `#C9CDD3`, `margin-top:4px`), `.ats-timeline-line` (`flex:1`, 1.5px wide, `#E7E9EC`),
`.ats-timeline-body` (flex column, gap 2px, `flex:1`), `.ats-timeline-body--spaced`
(`padding-bottom:1.125rem`), `.ats-timeline-title` (.8125rem, `--ats-ink`), `.ats-timeline-sub`
(.71875rem, `--ats-ink-subtle`), `.ats-empty-headline` (Urbanist 700, 1rem, `--ats-ink`),
`.ats-empty-body` (.8125rem, weight 300).

- [ ] **Step 8: Verify the partials compile**

Razor compiles at build time for this project, so a typo here is a build error.

Run: `dotnet build`
Expected: success. A `The name 'AvatarPalette' does not exist` error means
`Views/_ViewImports.cshtml` needs `@using Ats.Application.Common`; add it there rather than repeating
the using in every partial.

*Commit point: `feat: add shared presentation partials`*

---

## Task 12: Alerts and pager

There is deliberately **no `_PageHead.cshtml`**. The page head needs `IsSectionDefined` and
`RenderSectionAsync` to support a `PageActions` section, and neither is available inside a partial, so
that markup lives in `_Layout.cshtml` (Task 15 Step 4). `_PageHeader.cshtml` is deleted there too.

**Files:**
- Modify: `src/Ats.Web/Views/Shared/_Alerts.cshtml`
- Modify: `src/Ats.Web/Views/Shared/_Pager.cshtml`

- [ ] **Step 1: Restyle `_Alerts.cshtml`**

Keep the `TempData` contract exactly: controllers already set `Success`/`Error`/`Info` and must keep
working unchanged. Only the markup changes.

```razor
@{
    var alerts = new (string? Text, string Css, string Icon)[]
    {
        (TempData["Success"] as string, "alert-success", "check_circle"),
        (TempData["Error"] as string, "alert-danger", "error"),
        (TempData["Info"] as string, "alert-info", "info")
    };
}
@foreach (var a in alerts)
{
    if (!string.IsNullOrEmpty(a.Text))
    {
        <div class="alert @a.Css alert-dismissible fade show d-flex align-items-center gap-2" role="alert">
            <span class="ms ms-sm">@a.Icon</span>
            <span class="flex-grow-1">@a.Text</span>
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
        </div>
    }
}
```

- [ ] **Step 2: Restyle `_Pager.cshtml`**

Keeps the `PagerModel` contract (`Page`, `TotalPages`, `Action`, `Query`) so Jobs, Candidates and the
delivery log keep working untouched.

```razor
@model Ats.Web.Models.PagerModel
@if (Model.TotalPages > 1)
{
    <nav class="d-flex align-items-center justify-content-between" aria-label="Pagination">
        <span class="ats-muted" style="font-size:.78125rem;font-weight:300">
            Page @Model.Page of @Model.TotalPages
        </span>
        <div class="ats-pager">
            @if (Model.Page > 1)
            {
                <a class="ats-pager-btn" asp-action="@Model.Action" asp-route-page="@(Model.Page - 1)"
                   asp-all-route-data="Model.Query" aria-label="Previous page"><span class="ms ms-sm">chevron_left</span></a>
            }
            else
            {
                <span class="ats-pager-btn ats-pager-btn--disabled" aria-hidden="true"><span class="ms ms-sm">chevron_left</span></span>
            }

            <span class="ats-pager-btn ats-pager-btn--current" aria-current="page">@Model.Page</span>

            @if (Model.Page < Model.TotalPages)
            {
                <a class="ats-pager-btn" asp-action="@Model.Action" asp-route-page="@(Model.Page + 1)"
                   asp-all-route-data="Model.Query" aria-label="Next page"><span class="ms ms-sm">chevron_right</span></a>
            }
            else
            {
                <span class="ats-pager-btn ats-pager-btn--disabled" aria-hidden="true"><span class="ms ms-sm">chevron_right</span></span>
            }
        </div>
    </nav>
}
```

Add `.ats-pager-btn` (34px circle, 1px border, white, flex-centre, Urbanist 600 .8125rem),
`.ats-pager-btn--current` (Oxford Blue fill, white, no border) and `.ats-pager-btn--disabled`
(`--ats-ink-faint`, `pointer-events:none`) to `ats-components.css`.

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: success. `_Layout.cshtml` still references `<partial name="_PageHeader" />` at this point;
that resolves at runtime, not compile time, so Task 15 must land before browsing.

*Commit point: `feat: restyle alerts and pager`*

---

## Task 13: Sidebar navigation with groups, counts and branding

**Files:**
- Create: `src/Ats.Application/Shell/ShellSummary.cs`
- Create: `src/Ats.Application/Shell/IShellSummaryService.cs`
- Create: `src/Ats.Infrastructure/Shell/ShellSummaryService.cs`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`
- Modify: `src/Ats.Web/Views/Shared/Components/SidebarNav/Default.cshtml`

- [ ] **Step 1: Define the shell summary**

`src/Ats.Application/Shell/ShellSummary.cs`:

```csharp
namespace Ats.Application.Shell;

// Counts and flags the app shell needs on every authenticated page. One query batch, cached
// per request, so putting these in the sidebar does not multiply queries across the app.
public sealed record ShellSummary(
    int OpenJobs,
    int Candidates,
    int FailedDeliveries,
    int IdleApplications,
    int StaleDrafts)
{
    public int AttentionCount => FailedDeliveries + IdleApplications + StaleDrafts;
    public bool HasAttention => AttentionCount > 0;
    public bool IntegrationUnhealthy => FailedDeliveries > 0;
}
```

`src/Ats.Application/Shell/IShellSummaryService.cs`:

```csharp
namespace Ats.Application.Shell;

public interface IShellSummaryService
{
    Task<ShellSummary> GetAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement it**

`src/Ats.Infrastructure/Shell/ShellSummaryService.cs`. All five counts are tenant-scoped
automatically by the global query filter, so none of them needs a `TenantId` predicate.

```csharp
using Ats.Application.Shell;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Shell;

public sealed class ShellSummaryService : IShellSummaryService
{
    private const int IdleDays = 7;
    private const int StaleDraftDays = 7;

    private readonly AtsDbContext _db;
    private ShellSummary? _cached;

    public ShellSummaryService(AtsDbContext db) => _db = db;

    public async Task<ShellSummary> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var now = DateTimeOffset.UtcNow;
        var idleBefore = now.AddDays(-IdleDays);
        var draftBefore = now.AddDays(-StaleDraftDays);

        var openJobs = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);
        var candidates = await _db.Candidates.CountAsync(ct);
        var failed = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Failed, ct);

        // Idle = active, and nothing has happened since idleBefore. An application with no events
        // falls back to its AppliedAt.
        var idle = await _db.Applications
            .Where(a => a.Status == ApplicationStatus.Active)
            .Select(a => new
            {
                LastActivity = _db.ApplicationEvents
                    .Where(e => e.ApplicationId == a.Id)
                    .Max(e => (DateTimeOffset?)e.OccurredAt) ?? a.AppliedAt
            })
            .CountAsync(x => x.LastActivity < idleBefore, ct);

        var staleDrafts = await _db.Jobs
            .CountAsync(j => j.Status == JobStatus.Draft && j.CreatedAt < draftBefore, ct);

        return _cached = new ShellSummary(openJobs, candidates, failed, idle, staleDrafts);
    }
}
```

`Job` extends `TenantEntity` which extends `KeyedEntity`, which supplies `CreatedAt`, so the stale
draft count needs no new column.

The idle count relies on EF translating `Max(...) ?? a.AppliedAt` into
`ISNULL((SELECT MAX(OccurredAt) ...), AppliedAt)`. If that throws a translation error at runtime,
replace the single query with two: load the active application ids with their `AppliedAt`, load
`GroupBy(ApplicationId).Max(OccurredAt)` from `ApplicationEvents`, and combine in memory. Do not
switch to client evaluation over the whole table.

- [ ] **Step 3: Register it**

In `DependencyInjection.cs`:

```csharp
using Ats.Application.Shell;
using Ats.Infrastructure.Shell;
```

```csharp
        services.AddScoped<IShellSummaryService, ShellSummaryService>();
```

- [ ] **Step 4: Rewrite the view component**

`src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`. `NavItem` gains a `Group` and an optional
count selector; the item list is still the single place a new nav entry is added.

```csharp
using System.Security.Claims;
using Ats.Application.Branding;
using Ats.Application.Shell;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public enum NavGroup { None, Hiring, Setup, Admin }

public record NavItem(
    string Text,
    string Icon,
    string Controller,
    string Action,
    NavGroup Group = NavGroup.None,
    string? RequiredRole = null,
    Func<ShellSummary, int?>? Count = null,
    Func<ShellSummary, bool>? Alert = null);

public record SidebarNavModel(
    IReadOnlyList<IGrouping<NavGroup, NavItem>> Groups,
    string CurrentController,
    string UserName,
    string Role,
    TenantBranding Branding,
    ShellSummary Summary);

public class SidebarNavViewComponent : ViewComponent
{
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "space_dashboard", "Dashboard", "Index"),

        new("Jobs", "work_outline", "Jobs", "Index", NavGroup.Hiring, Count: s => s.OpenJobs),
        new("Candidates", "group", "Candidates", "Index", NavGroup.Hiring, Count: s => s.Candidates),

        new("Pipelines", "view_week", "Pipelines", "Index", NavGroup.Setup),
        new("Organisation", "apartment", "Organisation", "Index", NavGroup.Setup),
        new("Career site", "public", "CareerSite", "Index", NavGroup.Setup),

        new("Integrations", "cable", "Integration", "Index", NavGroup.Admin, AtsRole.Owner,
            Alert: s => s.IntegrationUnhealthy),
        new("Audit log", "history", "Audit", "Index", NavGroup.Admin, AtsRole.Owner),
    };

    private readonly ITenantBrandingService _branding;
    private readonly IShellSummaryService _summary;

    public SidebarNavViewComponent(ITenantBrandingService branding, IShellSummaryService summary)
    {
        _branding = branding;
        _summary = summary;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var current = RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var name = User.Identity?.Name is { Length: > 0 } n ? n : "User";
        var role = (User as ClaimsPrincipal)?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        var branding = await _branding.GetAsync();
        var summary = await _summary.GetAsync();

        var groups = Items
            .Where(i => i.RequiredRole is null || string.Equals(i.RequiredRole, role, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Group)
            .ToList();

        return View(new SidebarNavModel(groups, current, name, role, branding, summary));
    }
}
```

The `Organisation` and `CareerSite` controllers do not exist until Phase 3. `asp-controller` on a
missing controller produces an empty `href`, not a build error, so the sidebar renders with two dead
links between now and then. That is acceptable for one phase; note it in the Task 20 verification.

- [ ] **Step 5: Rewrite the sidebar view**

`src/Ats.Web/Views/Shared/Components/SidebarNav/Default.cshtml`:

```razor
@using Ats.Application.Common
@using Ats.Web.ViewComponents
@model SidebarNavModel
@{
    var labels = new Dictionary<NavGroup, string>
    {
        [NavGroup.Hiring] = "Hiring:",
        [NavGroup.Setup] = "Setup:",
        [NavGroup.Admin] = "Admin:"
    };
}
<aside class="ats-sidebar">
    <div class="ats-brand">
        <span class="ats-brand-mark">A</span>
        <span class="d-flex flex-column" style="line-height:1.1">
            <span class="ats-brand-name">ATS</span>
            <span class="ats-brand-sub">recruitment suite</span>
        </span>
    </div>

    <div class="ats-tenant-chip">
        <span class="ats-tenant-chip-mark">@AvatarPalette.Initials(Model.Branding.TenantName)</span>
        <span class="d-flex flex-column flex-grow-1" style="min-width:0">
            <span class="ats-tenant-chip-name">@Model.Branding.TenantName</span>
            <span class="ats-brand-sub">tenant</span>
        </span>
    </div>

    <nav class="ats-nav">
        @foreach (var group in Model.Groups)
        {
            <div class="ats-nav-group">
                @if (labels.TryGetValue(group.Key, out var label))
                {
                    <div class="ats-nav-label">@label</div>
                }
                @foreach (var item in group)
                {
                    var active = string.Equals(item.Controller, Model.CurrentController, StringComparison.OrdinalIgnoreCase) ? "active" : "";
                    var count = item.Count?.Invoke(Model.Summary);
                    var alert = item.Alert?.Invoke(Model.Summary) ?? false;
                    <a class="@active" asp-controller="@item.Controller" asp-action="@item.Action">
                        <span class="ms">@item.Icon</span>
                        <span>@item.Text</span>
                        @if (alert)
                        {
                            <span class="ats-nav-alert" title="Needs attention"></span>
                        }
                        else if (count is > 0)
                        {
                            <span class="ats-nav-count">@count</span>
                        }
                    </a>
                }
            </div>
        }
    </nav>

    <div class="ats-sidebar-user">
        <partial name="Partials/_Avatar" model="new Ats.Web.Models.Shared.AvatarModel(Model.UserName, 1.875)" />
        <span class="d-flex flex-column flex-grow-1" style="min-width:0">
            <span class="ats-sidebar-user-name">@Model.UserName</span>
            @if (!string.IsNullOrEmpty(Model.Role))
            {
                <span class="ats-sidebar-user-role">@Model.Role</span>
            }
        </span>
        <form asp-controller="Account" asp-action="Logout" method="post" class="m-0">
            <button type="submit" class="ats-sidebar-signout" title="Sign out" aria-label="Sign out">
                <span class="ms ms-sm">logout</span>
            </button>
        </form>
    </div>
</aside>
```

Add `.ats-sidebar-signout` to `ats-shell.css`: transparent background, no border,
`color: var(--ats-sidebar-muted)`, padding `.25rem`, `border-radius: var(--no-radius-sm)`, and on
hover `background: var(--ats-sidebar-hover); color: var(--ats-sidebar-fg)`.

- [ ] **Step 6: Verify build**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: grouped sidebar navigation with counts and branding`*

---

## Task 14: Global search and the topbar

**Files:**
- Create: `src/Ats.Application/Search/SearchResults.cs`
- Create: `src/Ats.Application/Search/IGlobalSearchService.cs`
- Create: `src/Ats.Infrastructure/Search/GlobalSearchService.cs`
- Create: `src/Ats.Web/Controllers/SearchController.cs`
- Create: `src/Ats.Web/Views/Search/_Results.cshtml`
- Create: `src/Ats.Web/ViewComponents/TopBarViewComponent.cs`
- Create: `src/Ats.Web/Views/Shared/Components/TopBar/Default.cshtml`
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Define the search contract**

`src/Ats.Application/Search/SearchResults.cs`:

```csharp
namespace Ats.Application.Search;

public sealed record JobHit(int Id, string Title, string ExternalRef, string Status);
public sealed record CandidateHit(int Id, string FullName, string Email);
public sealed record ApplicationHit(int Id, string CandidateName, string JobTitle, string ReferralCode);

public sealed record SearchResults(
    IReadOnlyList<JobHit> Jobs,
    IReadOnlyList<CandidateHit> Candidates,
    IReadOnlyList<ApplicationHit> Applications)
{
    public bool IsEmpty => Jobs.Count == 0 && Candidates.Count == 0 && Applications.Count == 0;

    public static SearchResults Empty { get; } = new([], [], []);
}
```

`src/Ats.Application/Search/IGlobalSearchService.cs`:

```csharp
namespace Ats.Application.Search;

public interface IGlobalSearchService
{
    // Caps results per category. Tenant scoping comes from the global query filter.
    Task<SearchResults> SearchAsync(string? term, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement it**

`src/Ats.Infrastructure/Search/GlobalSearchService.cs`. The term goes into `EF.Functions.Like`
through a parameter, so it cannot be injected into SQL; `%` and `_` are escaped so a user typing them
gets a literal match instead of a wildcard scan.

```csharp
using Ats.Application.Search;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Search;

public sealed class GlobalSearchService : IGlobalSearchService
{
    private const int PerCategory = 5;
    private const int MinTermLength = 2;

    private readonly AtsDbContext _db;
    public GlobalSearchService(AtsDbContext db) => _db = db;

    public async Task<SearchResults> SearchAsync(string? term, CancellationToken ct = default)
    {
        var trimmed = term?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinTermLength)
            return SearchResults.Empty;

        var pattern = $"%{Escape(trimmed)}%";

        var jobs = await _db.Jobs
            .Where(j => EF.Functions.Like(j.Title, pattern) || EF.Functions.Like(j.ExternalRef, pattern))
            .OrderByDescending(j => j.PublishedAt)
            .Take(PerCategory)
            .Select(j => new JobHit(j.Id, j.Title, j.ExternalRef, j.Status.ToString()))
            .ToListAsync(ct);

        var candidates = await _db.Candidates
            .Where(c => EF.Functions.Like(c.FirstName, pattern)
                     || EF.Functions.Like(c.LastName, pattern)
                     || EF.Functions.Like(c.Email, pattern))
            .OrderBy(c => c.LastName)
            .Take(PerCategory)
            .Select(c => new CandidateHit(c.Id, c.FirstName + " " + c.LastName, c.Email))
            .ToListAsync(ct);

        var applications = await _db.Applications
            .Where(a => a.SourceCode != null && EF.Functions.Like(a.SourceCode, pattern))
            .OrderByDescending(a => a.AppliedAt)
            .Take(PerCategory)
            .Select(a => new ApplicationHit(
                a.Id,
                a.Candidate!.FirstName + " " + a.Candidate.LastName,
                _db.Jobs.Where(j => j.Id == a.JobId).Select(j => j.Title).FirstOrDefault() ?? "",
                a.SourceCode!))
            .ToListAsync(ct);

        return new SearchResults(jobs, candidates, applications);
    }

    // LIKE metacharacters are escaped so a user typing % does not trigger a full scan.
    private static string Escape(string term) => term
        .Replace("[", "[[]")
        .Replace("%", "[%]")
        .Replace("_", "[_]");
}
```

- [ ] **Step 3: Register it**

```csharp
using Ats.Application.Search;
using Ats.Infrastructure.Search;
```

```csharp
        services.AddScoped<IGlobalSearchService, GlobalSearchService>();
```

- [ ] **Step 4: Add the controller**

`src/Ats.Web/Controllers/SearchController.cs`. Returns a partial for htmx. Authorization in this app
is per-controller, not global: `JobsController` carries a bare `[Authorize]` and
`IntegrationController` carries `[Authorize(Roles = AtsRole.Owner)]`. Search spans jobs, candidates
and referral codes, so it takes the bare `[Authorize]` and no role gate.

```csharp
using Ats.Application.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class SearchController : Controller
{
    private readonly IGlobalSearchService _search;
    public SearchController(IGlobalSearchService search) => _search = search;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
        => PartialView("_Results", await _search.SearchAsync(q, ct));
}
```

- [ ] **Step 5: Add the results partial**

`src/Ats.Web/Views/Search/_Results.cshtml`:

```razor
@model Ats.Application.Search.SearchResults
@if (Model.IsEmpty)
{
    <div class="ats-search-empty">No matches.</div>
}
else
{
    @if (Model.Jobs.Count > 0)
    {
        <div class="ats-search-group">
            <div class="ats-eyebrow ats-search-group-label">Jobs:</div>
            @foreach (var j in Model.Jobs)
            {
                <a class="ats-search-hit" asp-controller="Board" asp-action="Index" asp-route-jobId="@j.Id">
                    <span class="ms ms-sm">work_outline</span>
                    <span class="flex-grow-1">@j.Title</span>
                    <code>@j.ExternalRef</code>
                </a>
            }
        </div>
    }
    @if (Model.Candidates.Count > 0)
    {
        <div class="ats-search-group">
            <div class="ats-eyebrow ats-search-group-label">Candidates:</div>
            @foreach (var c in Model.Candidates)
            {
                <a class="ats-search-hit" asp-controller="Candidates" asp-action="Edit" asp-route-id="@c.Id">
                    <span class="ms ms-sm">person</span>
                    <span class="flex-grow-1">@c.FullName</span>
                    <span class="ats-faint" style="font-size:.71875rem">@c.Email</span>
                </a>
            }
        </div>
    }
    @if (Model.Applications.Count > 0)
    {
        <div class="ats-search-group">
            <div class="ats-eyebrow ats-search-group-label">Referral codes:</div>
            @foreach (var a in Model.Applications)
            {
                <a class="ats-search-hit" asp-controller="Applications" asp-action="Details" asp-route-id="@a.Id">
                    <span class="ms ms-sm">share</span>
                    <span class="flex-grow-1">@a.CandidateName <span class="ats-faint">· @a.JobTitle</span></span>
                    <code>@a.ReferralCode</code>
                </a>
            }
        </div>
    }
}
```

Add `.ats-search-group` (padding .5rem), `.ats-search-group-label` (padding .25rem .75rem),
`.ats-search-hit` (flex, gap .5rem, align centre, padding .5rem .75rem, radius `--no-radius-sm`,
`.8125rem`, `color: var(--ats-ink)`, hover `background: var(--ats-bg)`) and `.ats-search-empty`
(padding 1rem, `.8125rem`, `color: var(--ats-ink-subtle)`) to `ats-components.css`.

- [ ] **Step 6: Add the topbar view component**

`src/Ats.Web/ViewComponents/TopBarViewComponent.cs`. The breadcrumb is derived from the same nav
metadata the sidebar uses, so a new nav entry gets a breadcrumb for free.

```csharp
using Ats.Application.Shell;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public record TopBarModel(string CrumbRoot, string CrumbLeaf, ShellSummary Summary);

public class TopBarViewComponent : ViewComponent
{
    // Controller -> (group label, page label). Keep in step with SidebarNavViewComponent.Items.
    private static readonly Dictionary<string, (string Root, string Leaf)> Crumbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = ("Overview", "Dashboard"),
        ["Jobs"] = ("Hiring", "Jobs"),
        ["Board"] = ("Hiring", "Board"),
        ["Candidates"] = ("Hiring", "Candidates"),
        ["Applications"] = ("Hiring", "Application"),
        ["Pipelines"] = ("Setup", "Pipelines"),
        ["Organisation"] = ("Setup", "Organisation"),
        ["Departments"] = ("Setup", "Departments"),
        ["Locations"] = ("Setup", "Locations"),
        ["CareerSite"] = ("Setup", "Career site"),
        ["Integration"] = ("Admin", "Integrations"),
        ["Audit"] = ("Admin", "Audit log"),
    };

    private readonly IShellSummaryService _summary;
    public TopBarViewComponent(IShellSummaryService summary) => _summary = summary;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var controller = RouteData.Values["controller"]?.ToString() ?? "Dashboard";
        var crumb = Crumbs.TryGetValue(controller, out var c) ? c : ("Overview", controller);

        // ViewData["Title"] is the most specific label available, so prefer it for the leaf.
        var title = ViewData["Title"] as string;
        var leaf = string.IsNullOrWhiteSpace(title) ? crumb.Leaf : title;

        return View(new TopBarModel(crumb.Root, leaf, await _summary.GetAsync()));
    }
}
```

`ViewData` inside a view component resolves to the invoking view's `ViewData`, so
`ViewData["Title"]` is readable here. Confirm at runtime in Task 20; if it comes back null, fall back
to `crumb.Leaf`, which the code above already does.

- [ ] **Step 7: Add the topbar view**

`src/Ats.Web/Views/Shared/Components/TopBar/Default.cshtml`:

```razor
@model Ats.Web.ViewComponents.TopBarModel
<header class="ats-topbar">
    <div class="ats-crumbs">
        <span>@Model.CrumbRoot</span>
        <span class="ats-crumbs-sep">·</span>
        <span class="ats-crumbs-leaf">@Model.CrumbLeaf</span>
    </div>

    <div class="ats-topbar-search">
        <label class="ats-search" for="ats-global-search">
            <span class="ms ms-sm ats-muted">search</span>
            <input id="ats-global-search" name="q" type="search" autocomplete="off"
                   placeholder="Search candidates, jobs, refs…"
                   aria-label="Search candidates, jobs and referral codes"
                   hx-get="@Url.Action("Index", "Search")"
                   hx-trigger="keyup changed delay:250ms, search"
                   hx-target="#ats-search-results"
                   hx-swap="innerHTML" />
            <span class="ats-kbd">Ctrl K</span>
        </label>
        <div id="ats-search-results" class="ats-topbar-results" role="listbox"></div>
    </div>

    <button type="button" class="ats-icon-btn" id="ats-bell"
            title="@(Model.Summary.HasAttention ? $"{Model.Summary.AttentionCount} items need attention" : "Nothing needs attention")"
            aria-label="Notifications">
        <span class="ms ms-sm">notifications</span>
        @if (Model.Summary.HasAttention)
        {
            <span class="ats-icon-btn-dot"></span>
        }
    </button>

    @await RenderSectionAsync("TopBarAction", required: false)
</header>
```

`RenderSectionAsync` is not valid in a view component view. Replace that last line with a
`ViewData`-driven action instead:

```razor
    @if (ViewData["TopBarActionText"] is string actionText && ViewData["TopBarActionController"] is string ac)
    {
        <a class="btn btn-primary" asp-controller="@ac" asp-action="@(ViewData["TopBarActionAction"] as string ?? "Create")">
            <span class="ms ms-sm">@(ViewData["TopBarActionIcon"] as string ?? "add")</span> @actionText
        </a>
    }
```

Pages opt in by setting those four `ViewData` keys, for example in `Views/Jobs/Index.cshtml`:
`ViewData["TopBarActionText"] = "New job"; ViewData["TopBarActionController"] = "Jobs";`.

- [ ] **Step 8: Add the search keyboard shortcut**

Append to `src/Ats.Web/wwwroot/js/site.js`, preserving whatever is already in the file:

```javascript
// Ctrl/Cmd+K focuses global search; Escape clears the result list.
(function () {
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
            var input = document.getElementById('ats-global-search');
            if (!input) return;
            e.preventDefault();
            input.focus();
            input.select();
        }
        if (e.key === 'Escape') {
            var results = document.getElementById('ats-search-results');
            if (results) results.innerHTML = '';
        }
    });
    // Clicking away closes the result list.
    document.addEventListener('click', function (e) {
        var wrap = document.querySelector('.ats-topbar-search');
        var results = document.getElementById('ats-search-results');
        if (!wrap || !results) return;
        if (!wrap.contains(e.target)) results.innerHTML = '';
    });
})();
```

- [ ] **Step 9: Verify build**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: add global search and app topbar`*

---

## Task 15: Branding view component and the main layout

**Files:**
- Create: `src/Ats.Web/ViewComponents/BrandingViewComponent.cs`
- Create: `src/Ats.Web/Views/Shared/Components/Branding/Default.cshtml`
- Modify: `src/Ats.Web/Views/Shared/_Layout.cshtml`
- Modify: `src/Ats.Web/Views/_ViewImports.cshtml`
- Delete: `src/Ats.Web/Views/Shared/_PageHeader.cshtml`
- Delete: `src/Ats.Web/wwwroot/css/site.css`

- [ ] **Step 1: Add the branding view component**

`src/Ats.Web/ViewComponents/BrandingViewComponent.cs`:

```csharp
using Ats.Application.Branding;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.ViewComponents;

public class BrandingViewComponent : ViewComponent
{
    private readonly ITenantBrandingService _branding;
    public BrandingViewComponent(ITenantBrandingService branding) => _branding = branding;

    public async Task<IViewComponentResult> InvokeAsync() => View(await _branding.GetAsync());
}
```

- [ ] **Step 2: Add its view**

`src/Ats.Web/Views/Shared/Components/Branding/Default.cshtml`. `BrandColor.Normalize` runs again here
even though the service already normalized on read: this is the last gate before the value lands in
CSS, and a defence-in-depth check costs nothing. Anything invalid falls back to the default.

```razor
@using Ats.Application.Branding
@using Ats.Domain.Enums
@model TenantBranding
@{
    // Re-validate at the point of emission. This value is written into a style element.
    var accent = BrandColor.Normalize(Model.Accent) ?? BrandColor.DefaultAccent;
    var accentHover = BrandColor.Normalize(Model.AccentHover) ?? BrandColor.DefaultAccentHover;
    var light = Model.SidebarTheme == SidebarTheme.Light;
}
<style>
    :root {
        --ats-accent: @accent;
        --ats-accent-hover: @accentHover;
    @if (light)
    {
        @:--ats-sidebar-bg: #FFFFFF;
        @:--ats-sidebar-fg: #0C2340;
        @:--ats-sidebar-muted: #88909A;
        @:--ats-sidebar-label: #88909A;
        @:--ats-sidebar-border: #E1E3E6;
        @:--ats-sidebar-hover: #F5F6F7;
        @:--ats-sidebar-chip: #FAFBFC;
        @:--ats-sidebar-active: #EBF5FB;
    }
    }
</style>
```

If the `@if` inside the `<style>` block proves awkward for the Razor parser, build the declarations
into a string in the `@{ }` block and emit it with `@Html.Raw(css)` where `css` is assembled only from
the validated constants above and the two normalized colours. Never interpolate an unvalidated value.

- [ ] **Step 3: Add the usings so partials resolve**

`src/Ats.Web/Views/_ViewImports.cshtml` currently holds `@using Ats.Web`, `@using Ats.Web.Models` and
the two `@addTagHelper` lines. The `@addTagHelper *, Ats.Web` line is what makes `<vc:branding>`,
`<vc:sidebar-nav>` and `<vc:top-bar>` resolve, so leave it alone. Add three usings, giving:

```razor
@using Ats.Web
@using Ats.Web.Models
@using Ats.Web.Models.Shared
@using Ats.Web.ViewComponents
@using Ats.Application.Common
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Ats.Web
```

The file starts with a UTF-8 BOM; preserve it.

- [ ] **Step 4: Rewrite `_Layout.cshtml`**

The page-head markup lives here because it needs `IsSectionDefined`/`RenderSectionAsync`, which are
not available in a partial.

```razor
@{
    var title = ViewData["Title"] as string ?? "";
    var eyebrow = ViewData["Eyebrow"] as string;
    var heading = string.IsNullOrEmpty(title) || title.EndsWith('.') ? title : title + ".";
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@(string.IsNullOrEmpty(title) ? "ATS" : title + " - ATS")</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E%3Crect width='16' height='16' rx='3' fill='%230085CA'/%3E%3Cpath d='M5 5V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1h2v7H3V5h2zm1 0h4V4H6v1z' fill='white'/%3E%3C/svg%3E" />
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/material-symbols/index.css" />
    <link rel="stylesheet" href="~/css/ats-tokens.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-base.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-components.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-shell.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/Ats.Web.styles.css" asp-append-version="true" />
    @* Per-tenant overrides must come last so they win. *@
    <vc:branding></vc:branding>
</head>
<body>
    <div class="ats-shell">
        <vc:sidebar-nav></vc:sidebar-nav>
        <div class="ats-main">
            <vc:top-bar></vc:top-bar>
            <main class="ats-content ats-scroll">
                <partial name="_Alerts" />
                @if (!string.IsNullOrEmpty(heading))
                {
                    <div class="ats-pagehead">
                        <div class="ats-pagehead-text">
                            @if (!string.IsNullOrEmpty(eyebrow))
                            {
                                <span class="ats-eyebrow">@eyebrow</span>
                            }
                            <h1>@heading</h1>
                        </div>
                        @if (IsSectionDefined("PageActions"))
                        {
                            <div class="ats-pagehead-actions">@await RenderSectionAsync("PageActions", required: false)</div>
                        }
                    </div>
                }
                @RenderBody()
            </main>
        </div>
    </div>
    @* htmx swaps the candidate drawer in here (Phase 2). *@
    <div id="ats-drawer-host"></div>

    <script src="~/lib/jquery/dist/jquery.min.js"></script>
    <script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
    <script src="~/lib/htmx/dist/htmx.min.js"></script>
    <script src="~/js/site.js" asp-append-version="true"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

htmx moves from a per-page script to the layout because the topbar search needs it everywhere. Remove
the now-duplicate `<script src="~/lib/htmx/dist/htmx.min.js">` from
`src/Ats.Web/Views/Board/Index.cshtml`, leaving its SortableJS include in place.

- [ ] **Step 5: Delete the superseded files**

Delete `src/Ats.Web/Views/Shared/_PageHeader.cshtml` and `src/Ats.Web/wwwroot/css/site.css`. Confirm
nothing still references either:

```bash
grep -rn "_PageHeader\|css/site.css" src/ --include=*.cshtml --include=*.cs
```

Expected: no output. `_AuthLayout` and `_CareersLayout` still link `site.css` at this point, so fix
those references in Task 16 and Task 19 before deleting, or delete last. Order the work so the grep
comes back clean.

- [ ] **Step 6: Verify build**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: new app shell layout with per-tenant branding`*

---

## Task 16: Auth layout

**Files:**
- Modify: `src/Ats.Web/Views/Shared/_AuthLayout.cshtml`

- [ ] **Step 1: Rewrite it**

Anonymous pages have no tenant, so there is no `<vc:branding>` here: branding cannot be resolved
before sign-in and guessing it would leak which tenants exist.

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - ATS</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3E%3Crect width='16' height='16' rx='3' fill='%230085CA'/%3E%3Cpath d='M5 5V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1h2v7H3V5h2zm1 0h4V4H6v1z' fill='white'/%3E%3C/svg%3E" />
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/material-symbols/index.css" />
    <link rel="stylesheet" href="~/css/ats-tokens.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-base.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-components.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-shell.css" asp-append-version="true" />
</head>
<body>
    <div class="ats-auth">
        <div class="ats-auth-card">
            <div class="ats-auth-brand">
                <span class="ats-brand-mark">A</span>
                <span class="d-flex flex-column" style="line-height:1.1">
                    <span class="ats-brand-name" style="color:#fff">ATS</span>
                    <span class="ats-brand-sub">recruitment suite</span>
                </span>
            </div>
            <div class="card">
                <div class="card-body p-4">
                    <partial name="_Alerts" />
                    @RenderBody()
                </div>
            </div>
        </div>
    </div>
    <script src="~/lib/jquery/dist/jquery.min.js"></script>
    <script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
    <script src="~/lib/jquery-validation/dist/jquery.validate.min.js"></script>
    <script src="~/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

`bootstrap.bundle.min.js` is added because `_Alerts` now renders dismissible alerts, which need
Bootstrap's JS. It was previously absent from this layout.

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: success.

*Commit point: `feat: restyle auth layout`*

---

## Task 17: Careers layout keeps working

The public career site is redesigned in Phase 4, but it links `site.css`, which Task 15 deletes. This
task keeps it rendering sensibly in the meantime.

**Files:**
- Modify: `src/Ats.Web/Areas/Careers/Views/Shared/_CareersLayout.cshtml`

- [ ] **Step 1: Point it at the new stylesheets**

Swap the two `<link>` lines for `bootstrap-icons` and `site.css`, and replace the Bootstrap Icons
brand glyph. Leave everything else, including the script block, untouched.

```razor
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/lib/material-symbols/index.css" />
    <link rel="stylesheet" href="~/css/ats-tokens.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-base.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/ats-components.css" asp-append-version="true" />
```

and in the body:

```razor
    <nav class="navbar bg-white border-bottom mb-4">
        <div class="container">
            <span class="navbar-brand d-flex align-items-center gap-2">
                <span class="ats-brand-mark">A</span> Careers
            </span>
        </div>
    </nav>
```

Also update the favicon fill from `%234f46e5` to `%230085CA` to match the other two layouts.

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: success.

*Commit point: `chore: point careers layout at the new stylesheets`*

---

## Task 18: Verify the shell end to end

The migration must be applied before this task can run. If it has not been, stop and report.

- [ ] **Step 1: Confirm the database is current**

Ask the developer to confirm they ran the `database update` command. Do not run it.

- [ ] **Step 2: Run the app**

Run: `dotnet run --project src/Ats.Web`
Expected: listening on an https URL, no startup exception.

A startup `InvalidOperationException` naming `Organisation` or `CareerSite` would mean the sidebar's
dead links are worse than expected; `asp-controller` to a missing controller should render an empty
href silently. If it throws, comment those two `NavItem` entries out with a `// Phase 3` note.

- [ ] **Step 3: Walk the shell**

Sign in and confirm, on `/Dashboard`:

1. Sidebar is Oxford Blue, 252px, with `Hiring:`, `Setup:` and `Admin:` mono labels.
2. The active item has the 3px Sky Blue rail on its left edge.
3. Jobs and Candidates show numeric counts that match the Jobs and Candidates list totals.
4. Fonts are Urbanist for headings, Lexend for body, Sometype Mono for eyebrows. If everything is
   the system sans-serif, the `@font-face` paths in `ats-base.css` are wrong: check the browser
   network tab for 404s under `/lib/nowonline-fonts/`.
5. Icons render as glyphs, not empty boxes or ligature text like "work_outline". Text means the
   Material Symbols font did not load; check `/lib/material-symbols/`.
6. Topbar shows `Overview · Dashboard`, the search field and the bell.
7. `Ctrl`/`Cmd` + `K` focuses search. Typing two or more characters shows grouped results; clicking a
   job result lands on that job's board.
8. Page heading reads "Dashboard." with the trailing period, and the browser tab reads
   "Dashboard - ATS" without it.
9. Sign out works from the sidebar.

- [ ] **Step 4: Confirm nothing regressed**

Walk every existing screen and confirm it still functions, not just renders: `/Jobs` (search, status
filter, pager, publish, close), `/Board?jobId=` (drag a card between columns, confirm the move
persists after reload), `/Candidates` (search, add to job), `/Pipelines` (add and remove a stage,
save), `/Departments`, `/Locations`, `/Integration` (save settings, generate feed key, delivery log),
`/Audit`, `/Applications/Details/{id}` (resume download), and `/careers/{slug}` on the public side.

Any screen that renders but no longer submits is a regression from the layout change, most likely a
missing script. Report it rather than working around it.

- [ ] **Step 5: Confirm a non-Owner sees less**

Sign in as a Recruiter. Integrations and Audit log must be absent from the sidebar, and browsing
directly to `/Integration` must be refused exactly as it was before this phase.

- [ ] **Step 6: Confirm branding**

With the migration applied, set one tenant's `BrandAccentColor` to `#69CAA7` and
`BrandSidebarTheme` to `1` directly in the database, reload, and confirm the accent and the light
sidebar both take effect. Then set `BrandAccentColor` to `red; }` and confirm it falls back to
`#0085CA` with no broken CSS, which is the injection guard working.

*Commit point: `test: verify shell renders and existing screens still work`*

---

## Task 19: Icon sweep

**Files:**
- Modify: every view still containing `bi-`
- Delete: `src/Ats.Web/wwwroot/lib/bootstrap-icons/`

- [ ] **Step 1: Find every remaining reference**

Run:

```bash
grep -rn "bi bi-\|bi-" src/ --include=*.cshtml --include=*.cs --include=*.css
```

Expected, from the current tree: `Views/Jobs/Index.cshtml`, `Views/Board/Index.cshtml`,
`Views/Candidates/Index.cshtml`, `Views/Applications/Details.cshtml`, `Views/Pipelines/Index.cshtml`,
`Views/Pipelines/Form.cshtml`, `Views/Departments/Index.cshtml`, `Views/Locations/Index.cshtml`,
`Views/Integration/Index.cshtml`, `Views/Integration/Deliveries.cshtml`,
`Areas/Careers/Views/Jobs/Index.cshtml`, `Areas/Careers/Views/Jobs/Detail.cshtml`,
`Areas/Careers/Views/Jobs/ThankYou.cshtml`.

- [ ] **Step 2: Replace each one**

`<i class="bi bi-X"></i>` becomes `<span class="ms ms-sm">Y</span>` using this mapping. Do not
invent names outside it; every value below is a real Material Symbols Outlined ligature.

| Bootstrap Icon | Material Symbol |
|---|---|
| `bi-plus-lg` | `add` |
| `bi-arrow-left` | `arrow_back` |
| `bi-arrow-right` | `arrow_forward` |
| `bi-briefcase-fill`, `bi-briefcase` | `work_outline` |
| `bi-people` | `group` |
| `bi-person-circle` | `account_circle` |
| `bi-building` | `apartment` |
| `bi-geo-alt` | `place` |
| `bi-diagram-3` | `view_week` |
| `bi-plugin` | `cable` |
| `bi-journal-text` | `history` |
| `bi-speedometer2` | `space_dashboard` |
| `bi-check-circle` | `check_circle` |
| `bi-file-earmark-arrow-down` | `download` |

- [ ] **Step 3: Confirm the sweep is complete**

Run: `grep -rn "bi-" src/ --include=*.cshtml --include=*.cs --include=*.css`
Expected: no output.

- [ ] **Step 4: Delete the vendored font**

Delete `src/Ats.Web/wwwroot/lib/bootstrap-icons/` including
`font/bootstrap-icons.min.css`, `font/fonts/bootstrap-icons.woff`, `font/fonts/bootstrap-icons.woff2`.

Then confirm no layout still links it:

```bash
grep -rn "bootstrap-icons" src/ libman.json
```

Expected: no output.

- [ ] **Step 5: Verify**

Run: `dotnet build` then `dotnet run --project src/Ats.Web`
Expected: build succeeds; every screen renders glyphs with no empty boxes and no visible ligature
text.

*Commit point: `chore: complete Material Symbols icon sweep`*

---

## Task 20: Documentation

**Files:**
- Modify: `.claude/skills/ui/SKILL.md`
- Modify: `.claude/skills/multitenancy/SKILL.md`
- Modify: `.claude/skills/entities/SKILL.md`
- Modify: `.claude/skills/integration/SKILL.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Rewrite the UI skill's stack, tokens and components sections**

Replace the "Stack", "Design tokens" and "Shared components" sections with the reality after this
phase: Material Symbols not Bootstrap Icons; four `ats-*.css` files not `site.css`; the token layering
rule (views consume `--ats-*`, never `--no-*`); the three view components (`SidebarNav`, `TopBar`,
`Branding`); the seven partials in `Views/Shared/Partials`; the page-head `ViewData` contract
(`Title`, `Eyebrow`, `PageActions` section, the four `TopBarAction*` keys); and the rule that the
trailing heading period is added by the layout.

Update the "Add a new back-office page" checklist to say a `NavItem` now takes a `NavGroup` and an
optional count selector.

- [ ] **Step 2: Note the new columns in the other skills**

- `multitenancy`: `TenantSettings` now carries branding, resolved by request-scoped
  `ITenantBrandingService`; no new filter-bypass spot was introduced.
- `entities`: `JobApplication.Origin` is presentation-only, defaults to `Unknown` for rows predating
  the column, and is never read by the integration path.
- `integration`: `TenantSettings.FeedLastPulledAt` exists and will be written by the feed endpoint in
  Phase 3.

- [ ] **Step 3: Add the test project to `CLAUDE.md`**

Under "Build / run", add:

```
dotnet test
```

and add a row to the project table: `Ats.Tests` — xUnit tests for pure Application-layer logic
(no database).

- [ ] **Step 4: Verify**

Re-read each edited file and confirm no statement contradicts the code as built. The UI skill in
particular must not still tell a reader to use `bi-*` icons or `site.css`.

*Commit point: `docs: update skills and CLAUDE.md for the redesign foundation`*

---

## Phase 1 exit criteria

- [ ] `dotnet build` clean, `dotnet test` green (52 tests).
- [ ] `grep -rn "bi-" src/` and `grep -rn "css/site.css" src/` both return nothing.
- [ ] Every screen listed in Task 18 Step 4 renders in the new shell and still performs its action.
- [ ] A Recruiter cannot see or reach Integrations or Audit.
- [ ] An invalid `BrandAccentColor` falls back to the default with no broken CSS.
- [ ] Every `--no-*` value in `ats-tokens.css` traces to a documented source: the design system's
      `colors_and_type.css` for brand/neutral/semantic colours, or the redesign prototype's inline
      hex for the tint and stage-ramp tokens (per the spec's token table). None are invented.

Known-incomplete and intentional at the end of Phase 1: the sidebar's Organisation and Career site
links are dead until Phase 3; every screen body is still its old Bootstrap markup, restyled by the
token layer but not yet rebuilt to the prototype's layout. Phases 2 to 4 do that work.
