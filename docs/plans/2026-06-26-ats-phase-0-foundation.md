# ATS Phase 0 — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a new multi-tenant ATS solution where a company can register, sign in, and see an empty dashboard, with tenant isolation enforced automatically at the DbContext level.

**Architecture:** New .NET 10 solution at `D:\LiveProject\Ats` (separate from ReferralTool). Shared-DB multi-tenancy via a `TenantId` discriminator, EF Core global query filters, and a `SaveChanges` interceptor that stamps `TenantId` on insert. Authentication is abstracted behind `IIdentityService` with an ASP.NET Core Identity implementation (cookie auth for the MVC back-office, JWT for the API). Layering: Web/Api → Application services → repositories → EF Core in Infrastructure.

**Tech Stack:** .NET 10, ASP.NET Core MVC, ASP.NET Core Identity, EF Core (latest), SQL Server, Serilog, FluentValidation.

**Reference spec:** `D:\LiveProject\ReferralTool\docs\superpowers\specs\2026-06-26-ats-product-design.md` (Phase 0 = Section 12).

---

## Conventions for this plan

- **Verification = build + run.** There is no test project in this phase (decided: tests not required now). Each task ends with `dotnet build` and, where relevant, a manual run check.
- **Commits are manual.** Per the working restriction, the developer runs every `git commit` shown. An agent executing this plan must pause and ask the developer to commit, not run it.
- **Working directory** for all commands is `D:\LiveProject\Ats` unless stated otherwise.
- **Namespaces** are `Ats.<Project>` (e.g. `Ats.Domain`, `Ats.Infrastructure`).
- **Knowledge base.** This repo carries its own `CLAUDE.md` + `.claude/{rules,skills}` (Task 11),
  following spec Section 16. The final task of every phase refreshes them; in this phase that is Task 11
  (create) and Task 12 Step 7 (verify the skill index matches reality).

---

## File structure (created by this phase)

```
D:\LiveProject\Ats\
 ├─ Ats.sln
 ├─ .gitignore
 ├─ Directory.Build.props                         # shared TFM, nullable, langversion
 ├─ CLAUDE.md                                      # lean root guide + skill index (Task 11)
 ├─ .claude\
 │   ├─ rules\restrictions.md                      # AI never commits / migrates / deploys
 │   ├─ rules\multi-tenancy.md                     # tenancy invariant
 │   ├─ rules\migrations.md                        # EF migrations are manual
 │   ├─ skills\architecture\SKILL.md               # solution layout, layering, DI
 │   └─ skills\multitenancy\SKILL.md               # tenancy spine reference
 ├─ docs\
 │   ├─ specs\2026-06-26-ats-product-design.md     # copied product spec
 │   ├─ plans\2026-06-26-ats-phase-0-foundation.md # this plan (copied in)
 │   └─ integration\referraltool-contract.md       # copied frozen contract (Appendices A-C)
 ├─ src\
 │   ├─ Ats.Domain\
 │   │   ├─ Common\ITenantEntity.cs
 │   │   ├─ Common\TenantEntity.cs
 │   │   ├─ Common\KeyedEntity.cs
 │   │   ├─ Enums\TenantStatus.cs
 │   │   ├─ Enums\AtsRole.cs
 │   │   ├─ Entities\Tenant.cs
 │   │   ├─ Entities\TenantSettings.cs
 │   │   ├─ Entities\AppUser.cs
 │   │   ├─ Entities\PipelineTemplate.cs
 │   │   └─ Entities\PipelineStage.cs
 │   ├─ Ats.Application\
 │   │   ├─ Abstractions\ITenantContext.cs
 │   │   ├─ Abstractions\IIdentityService.cs
 │   │   ├─ Abstractions\ICurrentUser.cs
 │   │   ├─ Tenancy\TenantOnboardingService.cs
 │   │   ├─ Tenancy\RegisterTenantInput.cs
 │   │   ├─ Tenancy\RegisterTenantResult.cs
 │   │   └─ Tenancy\ReservedSlugs.cs
 │   ├─ Ats.Infrastructure\
 │   │   ├─ Persistence\AtsDbContext.cs
 │   │   ├─ Persistence\TenantSaveChangesInterceptor.cs
 │   │   ├─ Persistence\Configurations\*.cs
 │   │   ├─ Tenancy\HttpTenantContext.cs
 │   │   ├─ Identity\IdentityService.cs
 │   │   ├─ Identity\CurrentUser.cs
 │   │   └─ DependencyInjection.cs
 │   ├─ Ats.Web\        (MVC back-office; Areas added in later phases)
 │   ├─ Ats.Api\        (REST API skeleton)
 │   └─ Ats.Worker\     (background host skeleton)
```

`Ats.Domain` has **no** package dependencies. `Ats.Application` references `Ats.Domain`. `Ats.Infrastructure` references `Ats.Application` + `Ats.Domain`. `Ats.Web`/`Ats.Api`/`Ats.Worker` reference `Ats.Infrastructure` (and transitively the rest).

---

## Task 1: Create solution, projects, and references

**Files:**
- Create: `Ats.sln`, `Directory.Build.props`, `.gitignore`, and the 6 project folders under `src\`.

- [ ] **Step 1: Create the solution folder and git repo**

```bash
mkdir -p /d/LiveProject/Ats/src
cd /d/LiveProject/Ats
git init
dotnet new sln -n Ats
```

- [ ] **Step 2: Create `Directory.Build.props`**

Create `D:\LiveProject\Ats\Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create the projects**

```bash
cd /d/LiveProject/Ats
dotnet new classlib -n Ats.Domain        -o src/Ats.Domain
dotnet new classlib -n Ats.Application    -o src/Ats.Application
dotnet new classlib -n Ats.Infrastructure -o src/Ats.Infrastructure
dotnet new mvc       -n Ats.Web           -o src/Ats.Web
dotnet new webapi    -n Ats.Api           -o src/Ats.Api
dotnet new worker    -n Ats.Worker        -o src/Ats.Worker
```

- [ ] **Step 4: Add projects to the solution**

```bash
dotnet sln add src/Ats.Domain src/Ats.Application src/Ats.Infrastructure src/Ats.Web src/Ats.Api src/Ats.Worker
```

- [ ] **Step 5: Wire project references**

```bash
dotnet add src/Ats.Application    reference src/Ats.Domain
dotnet add src/Ats.Infrastructure reference src/Ats.Application src/Ats.Domain
dotnet add src/Ats.Web            reference src/Ats.Infrastructure
dotnet add src/Ats.Api            reference src/Ats.Infrastructure
dotnet add src/Ats.Worker         reference src/Ats.Infrastructure
```

- [ ] **Step 6: Add a .NET `.gitignore`**

```bash
cd /d/LiveProject/Ats
dotnet new gitignore
```

- [ ] **Step 7: Delete the default `Class1.cs` files from the class libraries**

Remove `src/Ats.Domain/Class1.cs`, `src/Ats.Application/Class1.cs`, `src/Ats.Infrastructure/Class1.cs` if present.

- [ ] **Step 8: Build to verify the skeleton compiles**

Run: `dotnet build Ats.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit** (developer runs this)

```bash
git add -A
git commit -m "chore: scaffold Ats solution and projects"
```

---

## Task 2: Add packages and Serilog + health-check skeleton

**Files:**
- Modify: `src/Ats.Infrastructure/Ats.Infrastructure.csproj`, `src/Ats.Web/Program.cs`, `src/Ats.Api/Program.cs`, `src/Ats.Worker/Program.cs`.

- [ ] **Step 1: Add EF Core + Identity + Serilog packages to Infrastructure**

```bash
cd /d/LiveProject/Ats
dotnet add src/Ats.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/Ats.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/Ats.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/Ats.Application package FluentValidation
```

- [ ] **Step 2: Add Serilog + health checks to Web and Api**

```bash
dotnet add src/Ats.Web package Serilog.AspNetCore
dotnet add src/Ats.Api package Serilog.AspNetCore
dotnet add src/Ats.Web package Microsoft.Extensions.Diagnostics.HealthChecks
dotnet add src/Ats.Api package Microsoft.Extensions.Diagnostics.HealthChecks
dotnet add src/Ats.Worker package Serilog.Extensions.Hosting
```

- [ ] **Step 3: Configure Serilog + `/health` in `src/Ats.Web/Program.cs`**

Add at the top of `Program.cs`, after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddHealthChecks();
```

After `var app = builder.Build();` add:

```csharp
app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
```

- [ ] **Step 4: Repeat the same Serilog + `/health` wiring in `src/Ats.Api/Program.cs`**

Use the identical snippets from Step 3.

- [ ] **Step 5: Build**

Run: `dotnet build Ats.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run the web app and hit health**

Run: `dotnet run --project src/Ats.Web`
In another terminal: `curl -k https://localhost:<port>/health`
Expected: `Healthy`. Stop the app (Ctrl+C).

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "chore: add EF/Identity/Serilog packages and health checks"
```

---

## Task 3: Domain base classes and enums

**Files:**
- Create: `src/Ats.Domain/Common/KeyedEntity.cs`, `Common/ITenantEntity.cs`, `Common/TenantEntity.cs`, `Enums/TenantStatus.cs`, `Enums/AtsRole.cs`.

- [ ] **Step 1: Create `KeyedEntity.cs`**

```csharp
namespace Ats.Domain.Common;

public abstract class KeyedEntity
{
    public int Id { get; set; }
    public Guid Key { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Create `ITenantEntity.cs`**

```csharp
namespace Ats.Domain.Common;

public interface ITenantEntity
{
    int TenantId { get; set; }
}
```

- [ ] **Step 3: Create `TenantEntity.cs`**

```csharp
namespace Ats.Domain.Common;

public abstract class TenantEntity : KeyedEntity, ITenantEntity
{
    public int TenantId { get; set; }
}
```

- [ ] **Step 4: Create the enums**

`Enums/TenantStatus.cs`:

```csharp
namespace Ats.Domain.Enums;

public enum TenantStatus
{
    Active = 0,
    Suspended = 1
}
```

`Enums/AtsRole.cs`:

```csharp
namespace Ats.Domain.Enums;

public static class AtsRole
{
    public const string Owner = "Owner";
    public const string Recruiter = "Recruiter";
    public const string HiringManager = "HiringManager";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Owner, Recruiter, HiringManager, Viewer };
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Ats.Domain`
Expected: Build succeeded.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat(domain): add base entity classes and role/status enums"
```

---

## Task 4: Tenant, settings, identity, and pipeline entities

**Files:**
- Create: `src/Ats.Domain/Entities/Tenant.cs`, `TenantSettings.cs`, `AppUser.cs`, `PipelineTemplate.cs`, `PipelineStage.cs`.

- [ ] **Step 1: Create `Tenant.cs`**

```csharp
using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class Tenant : KeyedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public TenantSettings? Settings { get; set; }
}
```

- [ ] **Step 2: Create `TenantSettings.cs`** (integration fields land here in Phase 3; created now so onboarding can attach a row)

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class TenantSettings : TenantEntity
{
    public bool IntegrationEnabled { get; set; }
    public string? ReferralToolBaseUrl { get; set; }
    public string? ReferralToolAuthToken { get; set; }
    public int? ReferralToolCustomerId { get; set; }
    public string CodeParameterName { get; set; } = "ref";
    public string? FeedApiKeyHash { get; set; }
}
```

- [ ] **Step 3: Create `AppUser.cs`** (extends Identity's user; lives in Domain as a plain class with the Identity base added in Infrastructure mapping)

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class AppUser : ITenantEntity
{
    public int Id { get; set; }
    public Guid Key { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // one of AtsRole.*
    public DateTimeOffset CreatedAt { get; set; }
}
```

> Note: Phase 0 uses a self-managed `AppUser` table behind `IIdentityService` rather than the full ASP.NET Identity schema, to keep the first cut small and the identity layer swappable. The Identity *abstraction* is what matters; the storage detail can be upgraded later without touching callers.

- [ ] **Step 4: Create `PipelineStage.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public enum StageOutcome { None = 0, Hired = 1, Rejected = 2 }

public class PipelineStage : TenantEntity
{
    public int PipelineTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsTerminal { get; set; }
    public StageOutcome TerminalOutcome { get; set; } = StageOutcome.None;
    public string? ReferralStatusOverride { get; set; }   // maps stage -> CandidateStatus; null = use Name
}
```

- [ ] **Step 5: Create `PipelineTemplate.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class PipelineTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public List<PipelineStage> Stages { get; set; } = new();
}
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Ats.Domain`
Expected: Build succeeded.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(domain): add tenant, settings, user, and pipeline entities"
```

---

## Task 5: Tenant context and current-user abstractions

**Files:**
- Create: `src/Ats.Application/Abstractions/ITenantContext.cs`, `ICurrentUser.cs`, `IIdentityService.cs`.
- Create: `src/Ats.Infrastructure/Tenancy/HttpTenantContext.cs`, `Identity/CurrentUser.cs`.

- [ ] **Step 1: Create `ITenantContext.cs`**

```csharp
namespace Ats.Application.Abstractions;

public interface ITenantContext
{
    // null only during onboarding / unauthenticated public requests with no slug
    int? CurrentTenantId { get; }
    bool HasTenant { get; }
}
```

- [ ] **Step 2: Create `ICurrentUser.cs`**

```csharp
namespace Ats.Application.Abstractions;

public interface ICurrentUser
{
    int? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
```

- [ ] **Step 3: Create `IIdentityService.cs`**

```csharp
namespace Ats.Application.Abstractions;

public record SignInResult(bool Succeeded, int? UserId, string? Role, string? Error);

public interface IIdentityService
{
    Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default);
    Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);
    string HashPassword(string password);
    bool VerifyPassword(string hash, string password);
}
```

- [ ] **Step 4: Create `HttpTenantContext.cs`**

Resolves tenant from the authenticated user's `tenant_id` claim first, then from a `{tenantSlug}` route value (used by the public career site in Phase 2). Slug→id resolution will be added in Phase 2; Phase 0 only needs the claim path.

```csharp
using Ats.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Ats.Infrastructure.Tenancy;

public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? CurrentTenantId
    {
        get
        {
            var claim = _accessor.HttpContext?.User?.FindFirst("tenant_id")?.Value;
            return int.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool HasTenant => CurrentTenantId is not null;
}
```

- [ ] **Step 5: Create `CurrentUser.cs`**

```csharp
using System.Security.Claims;
using Ats.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Ats.Infrastructure.Identity;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? UserId =>
        int.TryParse(_accessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id : null;

    public string? Role => _accessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;

    public bool IsAuthenticated => _accessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
```

- [ ] **Step 6: Build**

Run: `dotnet build Ats.sln`
Expected: Build succeeded.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat: add tenant context, current-user, and identity abstractions"
```

---

## Task 6: DbContext, global query filters, and TenantId interceptor

**Files:**
- Create: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs`, `Persistence/TenantSaveChangesInterceptor.cs`.

- [ ] **Step 1: Create `TenantSaveChangesInterceptor.cs`** (stamps `TenantId` on insert from the tenant context)

```csharp
using Ats.Application.Abstractions;
using Ats.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ats.Infrastructure.Persistence;

public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;

    public TenantSaveChangesInterceptor(ITenantContext tenant) => _tenant = tenant;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is ITenantEntity te && te.TenantId == 0)
            {
                if (_tenant.CurrentTenantId is not int id)
                    throw new InvalidOperationException(
                        $"Cannot insert {entry.Entity.GetType().Name}: no tenant in context. " +
                        "Tenant-scoped writes require a resolved tenant.");
                te.TenantId = id;
            }

            if (entry.Entity is KeyedEntity ke)
            {
                if (entry.State == EntityState.Added && ke.CreatedAt == default) ke.CreatedAt = now;
                if (entry.State == EntityState.Modified) ke.UpdatedAt = now;
            }
        }
    }
}
```

> The `TenantId == 0` guard lets onboarding create the `Tenant` itself (not an `ITenantEntity`) and then explicitly set `TenantId` on the `TenantSettings`/`AppUser`/template rows it inserts in the same unit of work, before a tenant claim exists. See Task 9.

- [ ] **Step 2: Create `AtsDbContext.cs`** with DbSets and global query filters

```csharp
using Ats.Application.Abstractions;
using Ats.Domain.Common;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Ats.Infrastructure.Persistence;

public class AtsDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AtsDbContext(DbContextOptions<AtsDbContext> options, ITenantContext tenant) : base(options)
        => _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PipelineTemplate> PipelineTemplates => Set<PipelineTemplate>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtsDbContext).Assembly);

        // Global query filter on every ITenantEntity: e.TenantId == currentTenant
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var param = Expression.Parameter(entityType.ClrType, "e");
                var prop = Expression.Property(param, nameof(ITenantEntity.TenantId));
                // current tenant captured via closure over _tenant
                var current = Expression.Call(
                    Expression.Constant(this), nameof(GetTenantIdOrZero), Type.EmptyTypes);
                var body = Expression.Equal(prop, current);
                var lambda = Expression.Lambda(body, param);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    // Used by the query filter. Returns 0 when no tenant -> filters everything out (fail closed).
    public int GetTenantIdOrZero() => _tenant.CurrentTenantId ?? 0;
}
```

> Fail-closed is deliberate: with no tenant resolved, `GetTenantIdOrZero()` returns 0, and since real `TenantId`s start at 1, tenant-scoped queries return nothing rather than leaking across tenants.

- [ ] **Step 3: Build**

Run: `dotnet build src/Ats.Infrastructure`
Expected: Build succeeded.

- [ ] **Step 4: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): add DbContext, global tenant query filters, and stamping interceptor"
```

---

## Task 7: EF configurations, DI registration, connection string, and initial migration

**Files:**
- Create: `src/Ats.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`, `AppUserConfiguration.cs`, `PipelineTemplateConfiguration.cs`.
- Create: `src/Ats.Infrastructure/DependencyInjection.cs`.
- Modify: `src/Ats.Web/appsettings.json`, `src/Ats.Web/Program.cs`.

- [ ] **Step 1: Create `TenantConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.HasKey(t => t.Id);
        b.HasAlternateKey(t => t.Key);
        b.Property(t => t.Name).IsRequired().HasMaxLength(200);
        b.Property(t => t.Slug).IsRequired().HasMaxLength(60);
        b.HasIndex(t => t.Slug).IsUnique();
        b.HasOne(t => t.Settings).WithOne().HasForeignKey<TenantSettings>(s => s.TenantId);
    }
}
```

- [ ] **Step 2: Create `AppUserConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).IsRequired().HasMaxLength(256);
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.Role).IsRequired().HasMaxLength(40);
        // email unique per tenant
        b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
    }
}
```

- [ ] **Step 3: Create `PipelineTemplateConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class PipelineTemplateConfiguration : IEntityTypeConfiguration<PipelineTemplate>
{
    public void Configure(EntityTypeBuilder<PipelineTemplate> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).IsRequired().HasMaxLength(120);
        b.HasMany(p => p.Stages).WithOne().HasForeignKey(s => s.PipelineTemplateId);
    }
}
```

- [ ] **Step 4: Create `DependencyInjection.cs`**

```csharp
using Ats.Application.Abstractions;
using Ats.Infrastructure.Identity;
using Ats.Infrastructure.Persistence;
using Ats.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ats.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<AtsDbContext>((sp, options) =>
        {
            options.UseSqlServer(config.GetConnectionString("AtsDb"));
            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        return services;
    }
}
```

- [ ] **Step 5: Add the connection string to `src/Ats.Web/appsettings.json`**

Add a `ConnectionStrings` block:

```json
{
  "ConnectionStrings": {
    "AtsDb": "Server=localhost;Database=AtsDb;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

- [ ] **Step 6: Register infrastructure in `src/Ats.Web/Program.cs`**

After the Serilog/health wiring, add:

```csharp
builder.Services.AddAtsInfrastructure(builder.Configuration);
```

Add `using Ats.Infrastructure;` at the top.

- [ ] **Step 7: Create the initial migration**

```bash
cd /d/LiveProject/Ats
dotnet tool install --global dotnet-ef   # if not already installed
dotnet ef migrations add InitialCreate --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

Expected: a `Migrations` folder appears under `src/Ats.Infrastructure` with `InitialCreate`.

- [ ] **Step 8: Build**

Run: `dotnet build Ats.sln`
Expected: Build succeeded.

- [ ] **Step 9: Apply the migration** (developer runs this — DB change is a manual operation)

```bash
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

Expected: `AtsDb` created with `Tenants`, `TenantSettings`, `Users`, `PipelineTemplates`, `PipelineStages`.

- [ ] **Step 10: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): EF configurations, DI, connection string, initial migration"
```

---

## Task 8: Identity service implementation (password hashing + credential validation)

**Files:**
- Create: `src/Ats.Infrastructure/Identity/IdentityService.cs`.

- [ ] **Step 1: Create `IdentityService.cs`** using ASP.NET Core Identity's `PasswordHasher`

```csharp
using Ats.Application.Abstractions;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private readonly AtsDbContext _db;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public IdentityService(AtsDbContext db) => _db = db;

    public string HashPassword(string password) => _hasher.HashPassword(new AppUser(), password);

    public bool VerifyPassword(string hash, string password) =>
        _hasher.VerifyHashedPassword(new AppUser(), hash, password) != PasswordVerificationResult.Failed;

    public async Task<int> CreateUserAsync(int tenantId, string email, string displayName, string password, string role, CancellationToken ct = default)
    {
        var user = new AppUser
        {
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PasswordHash = HashPassword(password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        // IgnoreQueryFilters: sign-in happens before a tenant is in context
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || !VerifyPassword(user.PasswordHash, password))
            return new SignInResult(false, null, null, "Invalid email or password.");

        return new SignInResult(true, user.Id, user.Role, null);
    }
}
```

> `ValidateCredentialsAsync` uses `IgnoreQueryFilters()` because at sign-in time there is no tenant claim yet, so the global filter would hide every user. The query is still safe: it matches a unique `(TenantId, Email)` row and returns the user's own `TenantId` for the issued claim.

- [ ] **Step 2: Build**

Run: `dotnet build src/Ats.Infrastructure`
Expected: Build succeeded.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): identity service with password hashing and credential validation"
```

---

## Task 9: Tenant onboarding service (register company + owner + default pipeline)

**Files:**
- Create: `src/Ats.Application/Tenancy/RegisterTenantInput.cs`, `RegisterTenantResult.cs`, `ReservedSlugs.cs`, `TenantOnboardingService.cs`.

- [ ] **Step 1: Create `RegisterTenantInput.cs`**

```csharp
namespace Ats.Application.Tenancy;

public record RegisterTenantInput(
    string CompanyName,
    string Slug,
    string OwnerName,
    string OwnerEmail,
    string Password);
```

- [ ] **Step 2: Create `RegisterTenantResult.cs`**

```csharp
namespace Ats.Application.Tenancy;

public record RegisterTenantResult(bool Succeeded, int TenantId, int OwnerUserId, string? Error);
```

- [ ] **Step 3: Create `ReservedSlugs.cs`**

```csharp
namespace Ats.Application.Tenancy;

public static class ReservedSlugs
{
    public static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "careers", "manage", "health", "www", "app", "static", "assets"
    };

    public static bool IsReserved(string slug) => Values.Contains(slug);
}
```

- [ ] **Step 4: Create `TenantOnboardingService.cs`**

This runs in one transaction: create the `Tenant`, then stamp `TenantId` explicitly on the settings, owner user, and seeded pipeline (the interceptor will not have a tenant claim during onboarding, so we set it by hand — the spec's explicit-stamping rule).

```csharp
using Ats.Application.Abstractions;
using Ats.Domain.Entities;

namespace Ats.Application.Tenancy;

public interface ITenantOnboardingService
{
    Task<RegisterTenantResult> RegisterAsync(RegisterTenantInput input, CancellationToken ct = default);
}

public sealed class TenantOnboardingService : ITenantOnboardingService
{
    private readonly IOnboardingStore _store;
    private readonly IIdentityService _identity;

    public TenantOnboardingService(IOnboardingStore store, IIdentityService identity)
    {
        _store = store;
        _identity = identity;
    }

    public async Task<RegisterTenantResult> RegisterAsync(RegisterTenantInput input, CancellationToken ct = default)
    {
        var slug = input.Slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(slug) || ReservedSlugs.IsReserved(slug))
            return new RegisterTenantResult(false, 0, 0, "That URL slug is not allowed.");

        if (await _store.SlugExistsAsync(slug, ct))
            return new RegisterTenantResult(false, 0, 0, "That URL slug is already taken.");

        var tenant = new Tenant { Name = input.CompanyName.Trim(), Slug = slug };
        var settings = new TenantSettings { CodeParameterName = "ref" };
        var defaultTemplate = BuildDefaultPipeline();
        var ownerHash = _identity.HashPassword(input.Password);

        var (tenantId, ownerId) = await _store.CreateTenantGraphAsync(
            tenant, settings, defaultTemplate, input.OwnerName.Trim(), input.OwnerEmail.Trim().ToLowerInvariant(), ownerHash, ct);

        return new RegisterTenantResult(true, tenantId, ownerId, null);
    }

    private static PipelineTemplate BuildDefaultPipeline()
    {
        return new PipelineTemplate
        {
            Name = "Standard hiring",
            Stages = new List<PipelineStage>
            {
                new() { Name = "Applied",        Order = 1 },
                new() { Name = "1st Interview",  Order = 2 },
                new() { Name = "2nd Interview",  Order = 3 },
                new() { Name = "Hired",          Order = 4, IsTerminal = true, TerminalOutcome = StageOutcome.Hired },
                new() { Name = "Rejected",       Order = 5, IsTerminal = true, TerminalOutcome = StageOutcome.Rejected },
            }
        };
    }
}
```

- [ ] **Step 5: Add the `IOnboardingStore` abstraction**

Create `src/Ats.Application/Tenancy/IOnboardingStore.cs`:

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Tenancy;

public interface IOnboardingStore
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    // Creates Tenant + Settings + default template + owner user in one transaction.
    // Returns (tenantId, ownerUserId). Stamps TenantId explicitly on all tenant-scoped rows.
    Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(
        Tenant tenant, TenantSettings settings, PipelineTemplate template,
        string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct);
}
```

- [ ] **Step 6: Implement `IOnboardingStore` in Infrastructure**

Create `src/Ats.Infrastructure/Tenancy/OnboardingStore.cs`:

```csharp
using Ats.Application.Tenancy;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Tenancy;

public sealed class OnboardingStore : IOnboardingStore
{
    private readonly AtsDbContext _db;

    public OnboardingStore(AtsDbContext db) => _db = db;

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, ct);

    public async Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(
        Tenant tenant, TenantSettings settings, PipelineTemplate template,
        string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);            // tenant.Id now assigned

        settings.TenantId = tenant.Id;
        template.TenantId = tenant.Id;
        foreach (var stage in template.Stages) stage.TenantId = tenant.Id;

        var owner = new AppUser
        {
            TenantId = tenant.Id,
            Email = ownerEmail,
            DisplayName = ownerName,
            PasswordHash = ownerPasswordHash,
            Role = Domain.Enums.AtsRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.TenantSettings.Add(settings);
        _db.PipelineTemplates.Add(template);
        _db.Users.Add(owner);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return (tenant.Id, owner.Id);
    }
}
```

- [ ] **Step 7: Register onboarding services in `DependencyInjection.cs`**

Add to `AddAtsInfrastructure`:

```csharp
services.AddScoped<IOnboardingStore, OnboardingStore>();
services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();
```

Add `using Ats.Application.Tenancy;` and `using Ats.Infrastructure.Tenancy;`.

- [ ] **Step 8: Build**

Run: `dotnet build Ats.sln`
Expected: Build succeeded.

- [ ] **Step 9: Commit** (developer)

```bash
git add -A
git commit -m "feat: tenant onboarding service with slug guard and default pipeline seeding"
```

---

## Task 10: Back-office auth UI (register, login, empty dashboard)

**Files:**
- Modify: `src/Ats.Web/Program.cs` (cookie auth).
- Create: `src/Ats.Web/Controllers/AccountController.cs`, `Controllers/DashboardController.cs`.
- Create: `src/Ats.Web/Models/RegisterViewModel.cs`, `Models/LoginViewModel.cs`.
- Create views: `Views/Account/Register.cshtml`, `Views/Account/Login.cshtml`, `Views/Dashboard/Index.cshtml`.

- [ ] **Step 1: Add cookie authentication in `Program.cs`**

Before `builder.Build()`:

```csharp
builder.Services.AddAuthentication("AtsCookie")
    .AddCookie("AtsCookie", o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
    });
builder.Services.AddAuthorization();
```

After `var app = builder.Build();` and before `app.MapControllerRoute`, ensure:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 2: Create the view models**

`Models/RegisterViewModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class RegisterViewModel
{
    [Required] public string CompanyName { get; set; } = "";
    [Required] public string Slug { get; set; } = "";
    [Required] public string OwnerName { get; set; } = "";
    [Required, EmailAddress] public string OwnerEmail { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}
```

`Models/LoginViewModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class LoginViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
}
```

- [ ] **Step 3: Create `AccountController.cs`**

```csharp
using System.Security.Claims;
using Ats.Application.Abstractions;
using Ats.Application.Tenancy;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

public class AccountController : Controller
{
    private readonly ITenantOnboardingService _onboarding;
    private readonly IIdentityService _identity;

    public AccountController(ITenantOnboardingService onboarding, IIdentityService identity)
    {
        _onboarding = onboarding;
        _identity = identity;
    }

    [HttpGet] public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _onboarding.RegisterAsync(
            new RegisterTenantInput(vm.CompanyName, vm.Slug, vm.OwnerName, vm.OwnerEmail, vm.Password));

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed.");
            return View(vm);
        }

        await SignInAsync(result.OwnerUserId, result.TenantId, "Owner");
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet] public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _identity.ValidateCredentialsAsync(vm.Email, vm.Password);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Invalid credentials.");
            return View(vm);
        }

        // tenant id is needed for the claim; fetch via a tenant lookup on the user
        await SignInByUserAsync(result.UserId!.Value, result.Role!);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AtsCookie");
        return RedirectToAction("Login");
    }

    private async Task SignInByUserAsync(int userId, string role)
    {
        // SignInResult does not carry tenantId; re-read it. Keep this minimal for Phase 0.
        var tenantId = await _identity is not null
            ? await GetTenantIdForUser(userId)
            : 0;
        await SignInAsync(userId, tenantId, role);
    }

    // Phase 0 helper: read tenant id for a user id, bypassing filters (no tenant in context yet).
    private Task<int> GetTenantIdForUser(int userId) => _tenantLookup(userId);

    // injected lookup set below via primary path; see Step 4 note.
    private Func<int, Task<int>> _tenantLookup = _ => Task.FromResult(0);

    private async Task SignInAsync(int userId, int tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
            new("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "AtsCookie");
        await HttpContext.SignInAsync("AtsCookie", new ClaimsPrincipal(identity));
    }
}
```

> **Step 4 note (resolve the tenant id at login cleanly):** the inline `_tenantLookup` placeholder above is a smell. Replace it by extending `SignInResult` to include `TenantId`. Do Step 4 below before building.

- [ ] **Step 4: Add `TenantId` to `SignInResult` and populate it**

In `src/Ats.Application/Abstractions/IIdentityService.cs`, change the record to:

```csharp
public record SignInResult(bool Succeeded, int? UserId, int? TenantId, string? Role, string? Error);
```

In `src/Ats.Infrastructure/Identity/IdentityService.cs`, update the two returns in `ValidateCredentialsAsync`:

```csharp
if (user is null || !VerifyPassword(user.PasswordHash, password))
    return new SignInResult(false, null, null, null, "Invalid email or password.");

return new SignInResult(true, user.Id, user.TenantId, user.Role, null);
```

Now simplify `AccountController.Login` to use the tenant id directly and **delete** the `SignInByUserAsync`, `GetTenantIdForUser`, and `_tenantLookup` members:

```csharp
var result = await _identity.ValidateCredentialsAsync(vm.Email, vm.Password);
if (!result.Succeeded)
{
    ModelState.AddModelError(string.Empty, result.Error ?? "Invalid credentials.");
    return View(vm);
}

await SignInAsync(result.UserId!.Value, result.TenantId!.Value, result.Role!);
return RedirectToAction("Index", "Dashboard");
```

- [ ] **Step 5: Create `DashboardController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    public IActionResult Index() => View();
}
```

- [ ] **Step 6: Create the views**

`Views/Account/Register.cshtml`:

```html
@model Ats.Web.Models.RegisterViewModel
<h1>Create your company account</h1>
<form asp-action="Register" method="post">
    <div asp-validation-summary="All"></div>
    <p>Company name <input asp-for="CompanyName" /></p>
    <p>URL slug <input asp-for="Slug" /> .ourats.com/careers</p>
    <p>Your name <input asp-for="OwnerName" /></p>
    <p>Work email <input asp-for="OwnerEmail" /></p>
    <p>Password <input asp-for="Password" /></p>
    <button type="submit">Create account</button>
</form>
```

`Views/Account/Login.cshtml`:

```html
@model Ats.Web.Models.LoginViewModel
<h1>Sign in</h1>
<form asp-action="Login" method="post">
    <div asp-validation-summary="All"></div>
    <p>Email <input asp-for="Email" /></p>
    <p>Password <input asp-for="Password" /></p>
    <button type="submit">Sign in</button>
</form>
```

`Views/Dashboard/Index.cshtml`:

```html
<h1>Dashboard</h1>
<p>Welcome. Jobs, pipelines, and candidates arrive in Phase 1.</p>
<form asp-controller="Account" asp-action="Logout" method="post">
    <button type="submit">Sign out</button>
</form>
```

- [ ] **Step 7: Build**

Run: `dotnet build Ats.sln`
Expected: Build succeeded, 0 errors. (If the `AccountController` still references removed members, finish Step 4's deletions.)

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): registration, login, cookie auth, empty dashboard"
```

---

## Task 11: Create the repo knowledge base (CLAUDE.md, rules, skills, docs)

Establishes the best-practice `CLAUDE.md` + `.claude/{rules,skills}` structure (spec Section 16) and
copies the spec, plan, and frozen contract into the repo so it is fully self-sufficient.

**Files:**
- Create: `CLAUDE.md`.
- Create: `.claude/rules/restrictions.md`, `.claude/rules/multi-tenancy.md`, `.claude/rules/migrations.md`.
- Create: `.claude/skills/architecture/SKILL.md`, `.claude/skills/multitenancy/SKILL.md`.
- Create: `docs/specs/2026-06-26-ats-product-design.md`, `docs/plans/2026-06-26-ats-phase-0-foundation.md`, `docs/integration/referraltool-contract.md`.

- [ ] **Step 1: Copy the spec, this plan, and the frozen contract into the repo**

- Copy `D:\LiveProject\ReferralTool\docs\superpowers\specs\2026-06-26-ats-product-design.md`
  to `D:\LiveProject\Ats\docs\specs\2026-06-26-ats-product-design.md`.
- Copy this plan to `D:\LiveProject\Ats\docs\plans\2026-06-26-ats-phase-0-foundation.md`.
- Copy the spec's "Appendix A / B / C" sections verbatim into
  `D:\LiveProject\Ats\docs\integration\referraltool-contract.md`.

> If the bootstrap already placed these files (recommended handoff), this step is a verify-only check.

- [ ] **Step 2: Create `CLAUDE.md`**

```markdown
# Ats — Claude Code Instructions

Multi-tenant ATS product. .NET 10 MVC + SQL Server + EF Core. Integrates with ReferralTool.

## Restricted actions (manual developer operations only)
Read `.claude/rules/restrictions.md`. Never run git commit/push/merge/rebase/reset, EF
`database update` / apply migrations, or any deploy/CI. Refuse and suggest the manual command instead.

## Project overview
Companies register as tenants, post jobs, run candidates through a configurable pipeline, host a
public career site, and push referral status updates to ReferralTool.
Solution: `Ats.sln` (projects under `src/`).

| Project | Purpose |
|---------|---------|
| `Ats.Domain` | Entities, enums, domain rules. No framework/EF dependencies. |
| `Ats.Application` | Use-case services, abstractions (`ITenantContext`, `IIdentityService`), validators. |
| `Ats.Infrastructure` | EF Core `AtsDbContext`, tenancy filters + interceptor, Identity impl, DI. |
| `Ats.Web` | MVC back-office (`/manage`) + public career site (Areas, later phases). |
| `Ats.Api` | REST API: vacancy feed + integration endpoints. |
| `Ats.Worker` | Background host: outbox delivery, notifications. |

## Build / run
```
dotnet build Ats.sln
dotnet run --project src/Ats.Web
```

## Architecture (strict layering)
Controllers -> Application services -> repositories -> EF Core (Infrastructure).
`Ats.Domain` has no EF/framework deps. Always `async/await`.

## Multi-tenancy (CRITICAL)
Shared DB + `TenantId` discriminator. Every tenant entity extends `TenantEntity` / implements
`ITenantEntity`. Isolation is enforced automatically by the global query filter + `SaveChanges`
interceptor in `AtsDbContext`; do NOT hand-set `TenantId` except in the documented onboarding path.
Bypass the filter only with `IgnoreQueryFilters()` at sign-in/onboarding (documented). Full rule:
`.claude/rules/multi-tenancy.md`.

## Database migrations
Applied MANUALLY by a developer. See `.claude/rules/migrations.md`.

## ReferralTool integration
Frozen contract in `docs/integration/referraltool-contract.md` — the source of truth. Do not
re-derive it from the ReferralTool repo.

## Conventions
- Auth behind `IIdentityService` (ASP.NET Core Identity impl); swappable later.
- No hardcoded secrets; config per environment, secrets outside source control.
- Code comments only when they say something the code/names cannot.

## Documentation maintenance (MANDATORY, after each phase)
1. Refresh the skill-index table below and any changed conventions.
2. Add/update the `.claude/skills/<domain>/SKILL.md` for the domain that phase built
   (Phase 1 -> `entities`, `pipeline`; Phase 2 -> `career-site`; Phase 3 -> `integration`;
   Phase 4 -> `notifications`/`audit`).
3. Keep `docs/specs` and `docs/plans` current.

## Skill files — read BEFORE exploring code
| Domain | Skill file | Covers |
|--------|-----------|--------|
| Architecture | `.claude/skills/architecture/SKILL.md` | Solution layout, layering, DI, where code goes |
| Multi-tenancy | `.claude/skills/multitenancy/SKILL.md` | `TenantEntity`, query filter, interceptor, onboarding stamping |
```

- [ ] **Step 3: Create `.claude/rules/restrictions.md`**

```markdown
# Restricted Actions (NEVER VIOLATE)

Manual developer-controlled operations only. The AI must refuse and suggest the manual command.

## Forbidden
- Git: `commit`, `push`, `pull`, `fetch`, `merge`, `rebase`, `reset`, `revert`, branch create/delete, `stash`, `tag`.
- Database: EF `database update`, apply/remove migrations, `database drop`, raw SQL execution, seeding.
- DevOps: `dotnet publish`/deploy, `az`, `docker`, `kubectl`, pipeline triggers.

## Allowed (read-only)
`git status`, `git diff`, `git log`, `git show`, `dotnet build`, `dotnet run`, creating EF migration
*files* with `dotnet ef migrations add` (but NOT applying them).

## When requested
Refuse, name the restriction, and give the exact manual command for the developer to run.
```

- [ ] **Step 4: Create `.claude/rules/multi-tenancy.md`**

```markdown
# Multi-Tenancy Rules (CRITICAL)

- Tenant identity is `int TenantId`. Every tenant-scoped entity extends `TenantEntity`
  (`Ats.Domain/Common/TenantEntity.cs`) or implements `ITenantEntity`.
- Isolation is automatic:
  - `AtsDbContext.OnModelCreating` applies a global query filter `e.TenantId == GetTenantIdOrZero()`
    to every `ITenantEntity`. No tenant in context => returns 0 => queries return nothing (fail closed).
  - `TenantSaveChangesInterceptor` stamps `TenantId` on insert from `ITenantContext`. Inserting a
    tenant entity with no tenant in context throws.
- Do NOT hand-set `TenantId` in normal code; let the interceptor do it.
- The ONLY places that bypass the filter (`IgnoreQueryFilters()`) or set `TenantId` by hand:
  - `IdentityService.ValidateCredentialsAsync` (sign-in: no tenant claim yet; matches unique `(TenantId, Email)`).
  - `OnboardingStore.CreateTenantGraphAsync` (creates the tenant graph before a tenant claim exists;
    stamps `TenantId` explicitly on settings/template/stages/owner). These are documented exceptions.
- Never expose a queryable that bypasses the filter outside those documented spots.
```

- [ ] **Step 5: Create `.claude/rules/migrations.md`**

```markdown
# EF Core Migration Rules

- Migrations live in `src/Ats.Infrastructure/Migrations`, context `AtsDbContext`.
- Create a migration (allowed):
  `dotnet ef migrations add <Name> --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`
- Applying migrations is a MANUAL developer action. The AI must NOT run `database update`.
  Provide the command for the developer:
  `dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`
- Name migrations in PascalCase describing the change (e.g. `AddJobAndApplication`).
```

- [ ] **Step 6: Create `.claude/skills/architecture/SKILL.md`**

```markdown
---
name: architecture
description: Ats solution layout, strict layering, DI registration, and where each kind of code belongs. Read before exploring the codebase.
---

# Ats Architecture

## Solution
`Ats.sln`, projects under `src/`:
- `Ats.Domain` — entities, enums, domain rules. No dependencies.
- `Ats.Application` — use-case services + abstractions (`ITenantContext`, `ICurrentUser`,
  `IIdentityService`, onboarding) + validators. References Domain.
- `Ats.Infrastructure` — `AtsDbContext`, EF configurations, `TenantSaveChangesInterceptor`,
  `HttpTenantContext`, `IdentityService`, `OnboardingStore`, `DependencyInjection`. References Application.
- `Ats.Web` — MVC back-office; cookie auth; controllers are thin.
- `Ats.Api` — REST API (feed + integration; built out in Phase 3).
- `Ats.Worker` — background host (outbox delivery; built out in Phase 3).

## Layering (strict)
Controllers -> Application services -> repositories -> EF Core. Domain has no framework deps.
Controllers hold no business logic. Always async/await.

## DI
Infrastructure wiring is centralised in `Ats.Infrastructure/DependencyInjection.cs`
(`AddAtsInfrastructure`). Register new services there. Hosts call `AddAtsInfrastructure(config)` in
`Program.cs`.

## Where things go
- New entity -> `Ats.Domain/Entities` (+ `IEntityTypeConfiguration` in
  `Ats.Infrastructure/Persistence/Configurations`).
- New use case -> an Application service behind an interface; impl/store in Infrastructure.
- New cross-cutting capability -> abstraction in `Ats.Application/Abstractions`, impl in Infrastructure.

## Conventions
- Cookie auth for back-office; JWT for Api (Phase 3+). Auth always behind `IIdentityService`.
- Timestamps via `KeyedEntity.CreatedAt/UpdatedAt`, stamped by the interceptor, stored UTC.
```

- [ ] **Step 7: Create `.claude/skills/multitenancy/SKILL.md`**

```markdown
---
name: multitenancy
description: The Ats tenancy spine — TenantEntity, global query filter, SaveChanges interceptor, tenant context resolution, and the documented filter-bypass spots. Read before touching any tenant-scoped data path.
---

# Ats Multi-Tenancy

## Model
Shared DB + `int TenantId` discriminator. Tenant-scoped entities extend
`Ats.Domain/Common/TenantEntity.cs` (which carries `TenantId` + `KeyedEntity` Id/Key/timestamps) or
implement `ITenantEntity`.

## Enforcement (automatic)
- **Query filter:** `AtsDbContext.OnModelCreating` adds `e.TenantId == GetTenantIdOrZero()` to every
  `ITenantEntity`. `GetTenantIdOrZero()` returns `ITenantContext.CurrentTenantId ?? 0`. Real ids start
  at 1, so "no tenant" filters everything out — fail closed, never leak.
- **Stamping:** `TenantSaveChangesInterceptor` sets `TenantId` on `Added` `ITenantEntity` rows from
  `ITenantContext`, and throws if none is resolved. Also stamps `CreatedAt`/`UpdatedAt`.

## Tenant resolution
`HttpTenantContext` reads the `tenant_id` claim (back-office/API). The career-site slug path
(`/careers/{tenantSlug}` -> tenant id) is added in Phase 2.

## Documented bypasses (the ONLY ones)
- `IdentityService.ValidateCredentialsAsync` — `IgnoreQueryFilters()` at sign-in (no claim yet).
- `OnboardingStore.CreateTenantGraphAsync` — creates the tenant graph and sets `TenantId` by hand on
  settings/template/stages/owner before a claim exists, inside one transaction.

## Rule
Outside those two spots: never `IgnoreQueryFilters()`, never hand-set `TenantId`, never expose an
unfiltered queryable. See `.claude/rules/multi-tenancy.md`.
```

- [ ] **Step 8: Build to confirm nothing broke (docs only, should be unaffected)**

Run: `dotnet build Ats.sln`
Expected: Build succeeded.

- [ ] **Step 9: Commit** (developer)

```bash
git add -A
git commit -m "docs: add CLAUDE.md, .claude rules + skills, and copied spec/plan/contract"
```

---

## Task 12: Manual end-to-end verification of Phase 0

**No new files.** This task confirms the foundation works and tenant isolation holds.

- [ ] **Step 1: Run the web app**

Run: `dotnet run --project src/Ats.Web`

- [ ] **Step 2: Register the first company**

Browse to `/Account/Register`. Submit: Company "Acme", slug "acme", owner name/email, a password.
Expected: redirected to `/Dashboard`, signed in.

- [ ] **Step 3: Verify the seed data in SQL**

Query the DB:

```sql
SELECT * FROM Tenants;
SELECT * FROM TenantSettings;
SELECT * FROM Users;
SELECT * FROM PipelineTemplates;
SELECT * FROM PipelineStages ORDER BY [Order];
```

Expected: one tenant ("acme"), one settings row with `CodeParameterName = 'ref'` and matching `TenantId`,
one Owner user, one "Standard hiring" template, five stages (Applied, 1st Interview, 2nd Interview,
Hired, Rejected) all carrying the same `TenantId`.

- [ ] **Step 4: Register a second company and confirm isolation**

Register "Beta" / slug "beta". Sign in as Beta's owner. Confirm the dashboard loads.
Then in SQL confirm Beta's rows carry a different `TenantId`. (Phase 1 will add tenant-scoped lists
that visibly prove the query filter; for Phase 0 the SQL check plus the interceptor guard is the gate.)

- [ ] **Step 5: Confirm the slug guard**

Attempt to register a third company with slug "acme" again, and once with slug "api".
Expected: both rejected with a friendly error ("already taken" / "not allowed").

- [ ] **Step 6: Hit `/health`**

Run: `curl -k https://localhost:<port>/health`
Expected: `Healthy`.

- [ ] **Step 7: Verify the knowledge base matches reality (per-phase doc gate)**

Confirm `CLAUDE.md`'s skill-index table lists `architecture` + `multitenancy`, both `SKILL.md` files
exist and describe what Phase 0 actually built (entity names, `AtsDbContext`, the two documented
bypass spots), and `docs/specs` + `docs/plans` + `docs/integration` are present. Fix any drift before
committing. (This is the standing "update docs after each phase" rule from spec Section 16; for Phase 0
the docs were authored in Task 11, so here it is a verify pass.)

- [ ] **Step 8: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 0 foundation complete and verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage (Section 12 Phase 0):** solution + 6 projects (Task 1), tenancy base + interceptor + filters (Tasks 3, 6), `IIdentityService` + Identity impl (Tasks 5, 8), RBAC roles defined + cookie auth (Tasks 3, 10; full policy handlers deferred to Phase 1 where role-gated screens first appear), DbContext + initial migration (Tasks 6, 7), tenant onboarding with slug uniqueness + reserved-slug guard (Task 9), default pipeline seeding on signup (Task 9), Serilog + `/health` skeleton (Task 2). Exit criteria verified in Task 12.
- **Spec coverage (Section 16 knowledge base):** `CLAUDE.md` + `.claude/rules` (restrictions, multi-tenancy, migrations) + `.claude/skills` (architecture, multitenancy) + copied spec/plan/contract created in Task 11; the per-phase "update docs" gate is verified in Task 12 Step 7. Future phases each add their domain `SKILL.md` and refresh the index as their own final task.
- **Placeholder scan:** the one interim smell (`_tenantLookup`) in Task 10 Step 3 is explicitly removed in Step 4 with the corrected `SignInResult` shape — no placeholders remain in the final state.
- **Type consistency:** `SignInResult` final shape `(bool, int?, int?, string?, string?)` is used consistently after Task 10 Step 4. `ITenantEntity.TenantId`, `KeyedEntity` timestamps, `AtsRole.*` constants, and `ITenantContext.CurrentTenantId` names match across tasks.
- **Note:** full per-role authorization policy handlers (Owner/Recruiter/HiringManager/Viewer enforcement on screens) are scaffolded as constants here and enforced in Phase 1, since Phase 0 has only the dashboard. This is intentional and called out in the spec coverage above.
