# ATS Phase 1 - Plan A: Config and Jobs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a recruiter define pipeline templates, manage departments and locations, and create then publish jobs that receive a stable per-tenant `ExternalRef`, all tenant-isolated and using the existing UI baseline.

**Architecture:** Adds soft delete to the tenancy spine, new domain entities (Department, Location, Job) and enums, EF configurations and one migration, Application services with repository interfaces (implemented in Infrastructure, matching the Phase 0 pattern), and thin Web controllers with Bootstrap views wired into the sidebar.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, SQL Server, FluentValidation, Bootstrap 5.

**Reference spec:** `docs/specs/2026-06-26-ats-phase-1-core-ats-design.md` (this is Plan A of two).

---

## Conventions for this plan

- **Verification = build + run.** No test project (repo convention). Each task ends with `dotnet build`; UI tasks add a manual run check.
- **Commits are manual.** The developer runs every `git commit`. An agent must pause and ask, not run it.
- **Migrations:** created with `dotnet ef migrations add` (allowed), applied by the developer with `dotnet ef database update` (never by the AI).
- **Working directory** is `D:\LiveProject\Ats`.
- **No em dashes and no emoji** in generated files.
- New services and repositories are registered in `Ats.Infrastructure/DependencyInjection.cs` (`AddAtsInfrastructure`), consistent with Phase 0.

---

## File structure (created or modified by Plan A)

```
src\Ats.Domain\
  Common\ISoftDeletable.cs                         # NEW
  Enums\JobStatus.cs                               # NEW
  Enums\EmploymentType.cs                          # NEW
  Entities\Department.cs                           # NEW
  Entities\Location.cs                             # NEW
  Entities\Job.cs                                  # NEW
  Entities\TenantSettings.cs                       # MODIFY: add LastJobNumber
src\Ats.Infrastructure\
  Persistence\AtsDbContext.cs                      # MODIFY: DbSets + soft-delete filter
  Persistence\Configurations\DepartmentConfiguration.cs   # NEW
  Persistence\Configurations\LocationConfiguration.cs     # NEW
  Persistence\Configurations\JobConfiguration.cs          # NEW
  Persistence\Repositories\DepartmentRepository.cs        # NEW
  Persistence\Repositories\LocationRepository.cs          # NEW
  Persistence\Repositories\PipelineTemplateRepository.cs  # NEW
  Persistence\Repositories\JobRepository.cs               # NEW
  Migrations\*_AddPipelineConfigAndJobs.cs         # NEW (generated)
  DependencyInjection.cs                           # MODIFY: register services + repos
src\Ats.Application\
  Departments\IDepartmentRepository.cs, DepartmentService.cs   # NEW
  Locations\ILocationRepository.cs, LocationService.cs         # NEW
  Pipelines\IPipelineTemplateRepository.cs, PipelineTemplateService.cs, PipelineEditModels.cs  # NEW
  Jobs\IJobRepository.cs, JobService.cs, JobModels.cs          # NEW
src\Ats.Web\
  Controllers\DepartmentsController.cs, LocationsController.cs, PipelinesController.cs, JobsController.cs  # NEW
  Models\ (view models per controller)             # NEW
  Views\Departments\*, Views\Locations\*, Views\Pipelines\*, Views\Jobs\*  # NEW
  ViewComponents\SidebarNavViewComponent.cs        # MODIFY: nav entries
```

---

## Task 1: Soft delete in the tenancy spine

**Files:**
- Create: `src/Ats.Domain/Common/ISoftDeletable.cs`.
- Modify: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs` (filter builder).

- [ ] **Step 1: Create `ISoftDeletable.cs`**

```csharp
namespace Ats.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
```

- [ ] **Step 2: Extend the global query filter in `AtsDbContext.OnModelCreating`**

Replace the filter-building block inside the `foreach` loop with this version, which ANDs `!IsDeleted` for soft-deletable types:

```csharp
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var param = Expression.Parameter(entityType.ClrType, "e");
                var prop = Expression.Property(param, nameof(ITenantEntity.TenantId));
                var current = Expression.Call(
                    Expression.Constant(this), nameof(GetTenantIdOrZero), Type.EmptyTypes);
                Expression body = Expression.Equal(prop, current);

                if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
                {
                    var notDeleted = Expression.Not(
                        Expression.Property(param, nameof(ISoftDeletable.IsDeleted)));
                    body = Expression.AndAlso(body, notDeleted);
                }

                var lambda = Expression.Lambda(body, param);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
```

`ISoftDeletable` is in `Ats.Domain.Common`, already imported by `AtsDbContext`.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (No entity implements `ISoftDeletable` yet, so behavior is unchanged.)

- [ ] **Step 4: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): add ISoftDeletable and soft-delete query filter"
```

---

## Task 2: Enums and the ExternalRef counter

**Files:**
- Create: `src/Ats.Domain/Enums/JobStatus.cs`, `src/Ats.Domain/Enums/EmploymentType.cs`.
- Modify: `src/Ats.Domain/Entities/TenantSettings.cs`.

- [ ] **Step 1: Create `JobStatus.cs`**

```csharp
namespace Ats.Domain.Enums;

public enum JobStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2
}
```

- [ ] **Step 2: Create `EmploymentType.cs`**

```csharp
namespace Ats.Domain.Enums;

public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Internship = 3,
    Temporary = 4
}
```

- [ ] **Step 3: Add `LastJobNumber` to `TenantSettings`**

In `src/Ats.Domain/Entities/TenantSettings.cs`, add this property to the class:

```csharp
    public int LastJobNumber { get; set; }
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(domain): add JobStatus/EmploymentType enums and LastJobNumber"
```

---

## Task 3: Department, Location, and Job entities + EF configs + DbSets

**Files:**
- Create: `src/Ats.Domain/Entities/Department.cs`, `Location.cs`, `Job.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Configurations/DepartmentConfiguration.cs`, `LocationConfiguration.cs`, `JobConfiguration.cs`.
- Modify: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs` (DbSets).

- [ ] **Step 1: Create `Department.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class Department : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create `Location.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class Location : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
}
```

- [ ] **Step 3: Create `Job.cs`**

```csharp
using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class Job : TenantEntity, ISoftDeletable
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public int PipelineTemplateId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
    public string ExternalRef { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}
```

- [ ] **Step 4: Create `DepartmentConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.Name).IsRequired().HasMaxLength(120);
        b.HasIndex(d => new { d.TenantId, d.Name });
    }
}
```

- [ ] **Step 5: Create `LocationConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.Name).IsRequired().HasMaxLength(120);
        b.Property(l => l.City).HasMaxLength(120);
        b.HasIndex(l => new { l.TenantId, l.Name });
    }
}
```

- [ ] **Step 6: Create `JobConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.HasKey(j => j.Id);
        b.Property(j => j.Title).IsRequired().HasMaxLength(200);
        b.Property(j => j.ExternalRef).IsRequired().HasMaxLength(36);
        b.HasIndex(j => new { j.TenantId, j.ExternalRef }).IsUnique();
        b.HasIndex(j => new { j.TenantId, j.Status });
        b.HasOne<Department>().WithMany().HasForeignKey(j => j.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(j => j.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PipelineTemplate>().WithMany().HasForeignKey(j => j.PipelineTemplateId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 7: Add DbSets in `AtsDbContext`**

In `AtsDbContext`, add these DbSets next to the existing ones:

```csharp
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Job> Jobs => Set<Job>();
```

- [ ] **Step 8: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit** (developer)

```bash
git add -A
git commit -m "feat(domain): add Department, Location, Job entities and EF configs"
```

---

## Task 4: Migration for Plan A schema

**Files:**
- Create: `src/Ats.Infrastructure/Migrations/*_AddPipelineConfigAndJobs.cs` (generated).

- [ ] **Step 1: Create the migration** (allowed; applying is the developer's job)

```bash
cd /d/LiveProject/Ats
dotnet ef migrations add AddPipelineConfigAndJobs --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```
Expected: a new migration appears under `src/Ats.Infrastructure/Migrations` creating `Departments`,
`Locations`, `Jobs`, the unique `(TenantId, ExternalRef)` index, and the `LastJobNumber` column on
`TenantSettings`.

- [ ] **Step 2: Sanity-check the migration**

```bash
grep -E "CreateTable|LastJobNumber|ExternalRef" src/Ats.Infrastructure/Migrations/*_AddPipelineConfigAndJobs.cs | head -20
```
Expected: tables `Departments`, `Locations`, `Jobs`; `LastJobNumber` added; `ExternalRef` present.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Developer applies the migration**

The developer runs (the AI must NOT):
```bash
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): migration for departments, locations, jobs"
```

---

## Task 5: Department and Location services + repositories + DI

**Files:**
- Create: `src/Ats.Application/Departments/IDepartmentRepository.cs`, `DepartmentService.cs`.
- Create: `src/Ats.Application/Locations/ILocationRepository.cs`, `LocationService.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/DepartmentRepository.cs`, `LocationRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `IDepartmentRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Departments;

public interface IDepartmentRepository
{
    Task<List<Department>> ListAsync(CancellationToken ct = default);
    Task<Department?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Department department, CancellationToken ct = default);
    Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `DepartmentService.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Departments;

public record OperationResult(bool Succeeded, string? Error)
{
    public static readonly OperationResult Ok = new(true, null);
    public static OperationResult Fail(string error) => new(false, error);
}

public interface IDepartmentService
{
    Task<List<Department>> ListAsync(CancellationToken ct = default);
    Task<Department?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(string name, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(int id, string name, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _repo;
    public DepartmentService(IDepartmentRepository repo) => _repo = repo;

    public Task<List<Department>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Department?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        await _repo.AddAsync(new Department { Name = name.Trim() }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(int id, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        var dept = await _repo.GetAsync(id, ct);
        if (dept is null) return OperationResult.Fail("Department not found.");
        dept.Name = name.Trim();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var dept = await _repo.GetAsync(id, ct);
        if (dept is null) return OperationResult.Fail("Department not found.");
        if (await _repo.IsReferencedByJobAsync(id, ct))
            return OperationResult.Fail("This department is used by one or more jobs and cannot be deleted.");
        // hard delete (departments are not soft-deletable per spec)
        dept.Name = dept.Name; // no-op to keep tracking; actual removal handled in repo
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
```

> The delete uses the repository's removal. To keep the service free of EF, add a `Remove` call in the
> repository. Replace the `DeleteAsync` body's removal section by calling a repository `Remove`:
> see Step 5 which adds `RemoveAsync`. Update `DeleteAsync` to:

```csharp
    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var dept = await _repo.GetAsync(id, ct);
        if (dept is null) return OperationResult.Fail("Department not found.");
        if (await _repo.IsReferencedByJobAsync(id, ct))
            return OperationResult.Fail("This department is used by one or more jobs and cannot be deleted.");
        await _repo.RemoveAsync(dept, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
```

- [ ] **Step 3: Add `RemoveAsync` to `IDepartmentRepository.cs`**

Add this member to the interface created in Step 1:

```csharp
    Task RemoveAsync(Department department, CancellationToken ct = default);
```

- [ ] **Step 4: Create `ILocationRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Locations;

public interface ILocationRepository
{
    Task<List<Location>> ListAsync(CancellationToken ct = default);
    Task<Location?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Location location, CancellationToken ct = default);
    Task RemoveAsync(Location location, CancellationToken ct = default);
    Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `LocationService.cs`**

```csharp
using Ats.Application.Departments; // for OperationResult
using Ats.Domain.Entities;

namespace Ats.Application.Locations;

public interface ILocationService
{
    Task<List<Location>> ListAsync(CancellationToken ct = default);
    Task<Location?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(string name, string? city, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(int id, string name, string? city, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class LocationService : ILocationService
{
    private readonly ILocationRepository _repo;
    public LocationService(ILocationRepository repo) => _repo = repo;

    public Task<List<Location>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Location?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(string name, string? city, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        await _repo.AddAsync(new Location { Name = name.Trim(), City = city?.Trim() }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(int id, string name, string? city, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return OperationResult.Fail("Name is required.");
        var loc = await _repo.GetAsync(id, ct);
        if (loc is null) return OperationResult.Fail("Location not found.");
        loc.Name = name.Trim();
        loc.City = city?.Trim();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var loc = await _repo.GetAsync(id, ct);
        if (loc is null) return OperationResult.Fail("Location not found.");
        if (await _repo.IsReferencedByJobAsync(id, ct))
            return OperationResult.Fail("This location is used by one or more jobs and cannot be deleted.");
        await _repo.RemoveAsync(loc, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
```

- [ ] **Step 6: Create `DepartmentRepository.cs`**

```csharp
using Ats.Application.Departments;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository : IDepartmentRepository
{
    private readonly AtsDbContext _db;
    public DepartmentRepository(AtsDbContext db) => _db = db;

    public Task<List<Department>> ListAsync(CancellationToken ct = default) =>
        _db.Departments.OrderBy(d => d.Name).ToListAsync(ct);

    public Task<Department?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(Department department, CancellationToken ct = default) =>
        await _db.Departments.AddAsync(department, ct);

    public Task RemoveAsync(Department department, CancellationToken ct = default)
    {
        _db.Departments.Remove(department);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.AnyAsync(j => j.DepartmentId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 7: Create `LocationRepository.cs`**

```csharp
using Ats.Application.Locations;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class LocationRepository : ILocationRepository
{
    private readonly AtsDbContext _db;
    public LocationRepository(AtsDbContext db) => _db = db;

    public Task<List<Location>> ListAsync(CancellationToken ct = default) =>
        _db.Locations.OrderBy(l => l.Name).ToListAsync(ct);

    public Task<Location?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task AddAsync(Location location, CancellationToken ct = default) =>
        await _db.Locations.AddAsync(location, ct);

    public Task RemoveAsync(Location location, CancellationToken ct = default)
    {
        _db.Locations.Remove(location);
        return Task.CompletedTask;
    }

    public Task<bool> IsReferencedByJobAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.AnyAsync(j => j.LocationId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 8: Register in `DependencyInjection.cs`**

In `AddAtsInfrastructure`, before `return services;`, add:

```csharp
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ILocationService, LocationService>();
```

Add the usings at the top: `using Ats.Application.Departments;`, `using Ats.Application.Locations;`,
`using Ats.Infrastructure.Persistence.Repositories;`.

- [ ] **Step 9: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit** (developer)

```bash
git add -A
git commit -m "feat: department and location services with repositories"
```

---

## Task 6: Department and Location controllers + views

**Files:**
- Create: `src/Ats.Web/Models/DepartmentViewModel.cs`, `LocationViewModel.cs`.
- Create: `src/Ats.Web/Controllers/DepartmentsController.cs`, `LocationsController.cs`.
- Create: `src/Ats.Web/Views/Departments/Index.cshtml`, `Form.cshtml`; `Views/Locations/Index.cshtml`, `Form.cshtml`.

- [ ] **Step 1: Create `DepartmentViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class DepartmentViewModel
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
}
```

- [ ] **Step 2: Create `LocationViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class LocationViewModel
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(120)] public string? City { get; set; }
}
```

- [ ] **Step 3: Create `DepartmentsController.cs`**

```csharp
using Ats.Application.Departments;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class DepartmentsController : Controller
{
    private readonly IDepartmentService _service;
    public DepartmentsController(IDepartmentService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet] public IActionResult Create() => View("Form", new DepartmentViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(DepartmentViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.CreateAsync(vm.Name);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Department created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var d = await _service.GetAsync(id);
        if (d is null) return NotFound();
        return View("Form", new DepartmentViewModel { Id = d.Id, Name = d.Name });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(DepartmentViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.UpdateAsync(vm.Id, vm.Name);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Department updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Department deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 4: Create `Views/Departments/Index.cshtml`**

```cshtml
@model List<Ats.Domain.Entities.Department>
@{ ViewData["Title"] = "Departments"; }
<div class="mb-3"><a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New department</a></div>
<table class="table table-hover bg-white">
    <thead><tr><th>Name</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var d in Model)
    {
        <tr>
            <td>@d.Name</td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@d.Id">Edit</a>
                <form asp-action="Delete" asp-route-id="@d.Id" method="post" class="d-inline"
                      onsubmit="return confirm('Delete this department?');">
                    <button class="btn btn-sm btn-outline-danger" type="submit">Delete</button>
                </form>
            </td>
        </tr>
    }
    @if (Model.Count == 0) { <tr><td colspan="2" class="text-muted">No departments yet.</td></tr> }
    </tbody>
</table>
```

- [ ] **Step 5: Create `Views/Departments/Form.cshtml`**

```cshtml
@model Ats.Web.Models.DepartmentViewModel
@{ ViewData["Title"] = Model.Id == 0 ? "New department" : "Edit department"; }
<form asp-action="@(Model.Id == 0 ? "Create" : "Edit")" method="post" class="col-md-6">
    <div asp-validation-summary="All" class="text-danger small mb-2"></div>
    <input type="hidden" asp-for="Id" />
    <div class="mb-3">
        <label asp-for="Name" class="form-label">Name</label>
        <input asp-for="Name" class="form-control" />
        <span asp-validation-for="Name" class="text-danger small"></span>
    </div>
    <button type="submit" class="btn btn-primary">Save</button>
    <a class="btn btn-link" asp-action="Index">Cancel</a>
</form>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 6: Create `LocationsController.cs`**

```csharp
using Ats.Application.Locations;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class LocationsController : Controller
{
    private readonly ILocationService _service;
    public LocationsController(ILocationService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet] public IActionResult Create() => View("Form", new LocationViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(LocationViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.CreateAsync(vm.Name, vm.City);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Location created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var l = await _service.GetAsync(id);
        if (l is null) return NotFound();
        return View("Form", new LocationViewModel { Id = l.Id, Name = l.Name, City = l.City });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(LocationViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.UpdateAsync(vm.Id, vm.Name, vm.City);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Location updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Location deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 7: Create `Views/Locations/Index.cshtml`**

```cshtml
@model List<Ats.Domain.Entities.Location>
@{ ViewData["Title"] = "Locations"; }
<div class="mb-3"><a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New location</a></div>
<table class="table table-hover bg-white">
    <thead><tr><th>Name</th><th>City</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var l in Model)
    {
        <tr>
            <td>@l.Name</td>
            <td>@l.City</td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@l.Id">Edit</a>
                <form asp-action="Delete" asp-route-id="@l.Id" method="post" class="d-inline"
                      onsubmit="return confirm('Delete this location?');">
                    <button class="btn btn-sm btn-outline-danger" type="submit">Delete</button>
                </form>
            </td>
        </tr>
    }
    @if (Model.Count == 0) { <tr><td colspan="3" class="text-muted">No locations yet.</td></tr> }
    </tbody>
</table>
```

- [ ] **Step 8: Create `Views/Locations/Form.cshtml`**

```cshtml
@model Ats.Web.Models.LocationViewModel
@{ ViewData["Title"] = Model.Id == 0 ? "New location" : "Edit location"; }
<form asp-action="@(Model.Id == 0 ? "Create" : "Edit")" method="post" class="col-md-6">
    <div asp-validation-summary="All" class="text-danger small mb-2"></div>
    <input type="hidden" asp-for="Id" />
    <div class="mb-3">
        <label asp-for="Name" class="form-label">Name</label>
        <input asp-for="Name" class="form-control" />
        <span asp-validation-for="Name" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="City" class="form-label">City</label>
        <input asp-for="City" class="form-control" />
        <span asp-validation-for="City" class="text-danger small"></span>
    </div>
    <button type="submit" class="btn btn-primary">Save</button>
    <a class="btn btn-link" asp-action="Index">Cancel</a>
</form>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 9: Build and run**

Run: `dotnet build` (expected 0 errors), then `dotnet run --project src/Ats.Web` and sign in.
Browse `/Departments` and `/Locations`: create, edit, and delete a record. Stop the app.

- [ ] **Step 10: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): departments and locations CRUD screens"
```

---

## Task 7: Pipeline template service + repository + DI

**Files:**
- Create: `src/Ats.Application/Pipelines/PipelineEditModels.cs`, `IPipelineTemplateRepository.cs`, `PipelineTemplateService.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/PipelineTemplateRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `PipelineEditModels.cs`** (input shapes for editing a template and its stages)

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public record StageInput(
    int? Id, string Name, int Order, bool IsTerminal, StageOutcome TerminalOutcome,
    string? ReferralStatusOverride, bool Delete);

public record PipelineTemplateInput(int? Id, string Name, List<StageInput> Stages);
```

- [ ] **Step 2: Create `IPipelineTemplateRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public interface IPipelineTemplateRepository
{
    Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default);
    Task<PipelineTemplate?> GetWithStagesAsync(int id, CancellationToken ct = default);
    Task AddAsync(PipelineTemplate template, CancellationToken ct = default);
    Task RemoveStagesAsync(IEnumerable<PipelineStage> stages, CancellationToken ct = default);
    Task RemoveAsync(PipelineTemplate template, CancellationToken ct = default);
    Task<bool> IsUsedByJobAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `PipelineTemplateService.cs`**

```csharp
using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public interface IPipelineTemplateService
{
    Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default);
    Task<PipelineTemplate?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> SaveAsync(PipelineTemplateInput input, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class PipelineTemplateService : IPipelineTemplateService
{
    private readonly IPipelineTemplateRepository _repo;
    public PipelineTemplateService(IPipelineTemplateRepository repo) => _repo = repo;

    public Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<PipelineTemplate?> GetAsync(int id, CancellationToken ct = default) => _repo.GetWithStagesAsync(id, ct);

    public async Task<OperationResult> SaveAsync(PipelineTemplateInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return OperationResult.Fail("Template name is required.");
        var kept = input.Stages.Where(s => !s.Delete).ToList();
        if (kept.Count == 0) return OperationResult.Fail("A template needs at least one stage.");

        PipelineTemplate template;
        if (input.Id is int id)
        {
            template = await _repo.GetWithStagesAsync(id, ct) ?? new PipelineTemplate();
            if (template.Id == 0) return OperationResult.Fail("Template not found.");
            template.Name = input.Name.Trim();

            // remove stages flagged delete or no longer present
            var keptIds = kept.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
            var toRemove = template.Stages.Where(s => !keptIds.Contains(s.Id)).ToList();
            if (toRemove.Count > 0) await _repo.RemoveStagesAsync(toRemove, ct);

            foreach (var s in kept)
            {
                var existing = s.Id is null ? null : template.Stages.FirstOrDefault(x => x.Id == s.Id);
                if (existing is null)
                    template.Stages.Add(MapNewStage(s));
                else
                    ApplyStage(existing, s);
            }
        }
        else
        {
            template = new PipelineTemplate { Name = input.Name.Trim() };
            foreach (var s in kept) template.Stages.Add(MapNewStage(s));
            await _repo.AddAsync(template, ct);
        }

        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var template = await _repo.GetWithStagesAsync(id, ct);
        if (template is null) return OperationResult.Fail("Template not found.");
        if (await _repo.IsUsedByJobAsync(id, ct))
            return OperationResult.Fail("This template is used by one or more jobs and cannot be deleted.");
        await _repo.RemoveAsync(template, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    private static PipelineStage MapNewStage(StageInput s) => new()
    {
        Name = s.Name.Trim(),
        Order = s.Order,
        IsTerminal = s.IsTerminal,
        TerminalOutcome = s.IsTerminal ? s.TerminalOutcome : StageOutcome.None,
        ReferralStatusOverride = string.IsNullOrWhiteSpace(s.ReferralStatusOverride) ? null : s.ReferralStatusOverride.Trim()
    };

    private static void ApplyStage(PipelineStage existing, StageInput s)
    {
        existing.Name = s.Name.Trim();
        existing.Order = s.Order;
        existing.IsTerminal = s.IsTerminal;
        existing.TerminalOutcome = s.IsTerminal ? s.TerminalOutcome : StageOutcome.None;
        existing.ReferralStatusOverride = string.IsNullOrWhiteSpace(s.ReferralStatusOverride) ? null : s.ReferralStatusOverride.Trim();
    }
}
```

- [ ] **Step 4: Create `PipelineTemplateRepository.cs`**

```csharp
using Ats.Application.Pipelines;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class PipelineTemplateRepository : IPipelineTemplateRepository
{
    private readonly AtsDbContext _db;
    public PipelineTemplateRepository(AtsDbContext db) => _db = db;

    public Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default) =>
        _db.PipelineTemplates.OrderBy(t => t.Name).ToListAsync(ct);

    public Task<PipelineTemplate?> GetWithStagesAsync(int id, CancellationToken ct = default) =>
        _db.PipelineTemplates.Include(t => t.Stages).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(PipelineTemplate template, CancellationToken ct = default) =>
        await _db.PipelineTemplates.AddAsync(template, ct);

    public Task RemoveStagesAsync(IEnumerable<PipelineStage> stages, CancellationToken ct = default)
    {
        _db.PipelineStages.RemoveRange(stages);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(PipelineTemplate template, CancellationToken ct = default)
    {
        _db.PipelineStages.RemoveRange(template.Stages);
        _db.PipelineTemplates.Remove(template);
        return Task.CompletedTask;
    }

    public Task<bool> IsUsedByJobAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.AnyAsync(j => j.PipelineTemplateId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Register in `DependencyInjection.cs`**

Add before `return services;`:

```csharp
        services.AddScoped<IPipelineTemplateRepository, PipelineTemplateRepository>();
        services.AddScoped<IPipelineTemplateService, PipelineTemplateService>();
```

Add `using Ats.Application.Pipelines;` at the top.

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat: pipeline template service with stage editing"
```

---

## Task 8: Pipelines controller + views (stage editor)

**Files:**
- Create: `src/Ats.Web/Models/PipelineEditViewModel.cs`.
- Create: `src/Ats.Web/Controllers/PipelinesController.cs`.
- Create: `src/Ats.Web/Views/Pipelines/Index.cshtml`, `Form.cshtml`.

- [ ] **Step 1: Create `PipelineEditViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using Ats.Domain.Entities;

namespace Ats.Web.Models;

public class StageRow
{
    public int? Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public int Order { get; set; }
    public bool IsTerminal { get; set; }
    public StageOutcome TerminalOutcome { get; set; } = StageOutcome.None;
    [StringLength(120)] public string? ReferralStatusOverride { get; set; }
    public bool Delete { get; set; }
}

public class PipelineEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public List<StageRow> Stages { get; set; } = new();
}
```

- [ ] **Step 2: Create `PipelinesController.cs`**

```csharp
using Ats.Application.Pipelines;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class PipelinesController : Controller
{
    private readonly IPipelineTemplateService _service;
    public PipelinesController(IPipelineTemplateService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet]
    public IActionResult Create() => View("Form", new PipelineEditViewModel
    {
        Stages = new() { new StageRow { Name = "Applied", Order = 1 } }
    });

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var t = await _service.GetAsync(id);
        if (t is null) return NotFound();
        var vm = new PipelineEditViewModel
        {
            Id = t.Id,
            Name = t.Name,
            Stages = t.Stages.OrderBy(s => s.Order).Select(s => new StageRow
            {
                Id = s.Id, Name = s.Name, Order = s.Order, IsTerminal = s.IsTerminal,
                TerminalOutcome = s.TerminalOutcome, ReferralStatusOverride = s.ReferralStatusOverride
            }).ToList()
        };
        return View("Form", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Save(PipelineEditViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);

        var input = new PipelineTemplateInput(
            vm.Id,
            vm.Name,
            vm.Stages.Select(s => new StageInput(
                s.Id, s.Name, s.Order, s.IsTerminal, s.TerminalOutcome, s.ReferralStatusOverride, s.Delete)).ToList());

        var result = await _service.SaveAsync(input);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Pipeline saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Pipeline deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 3: Create `Views/Pipelines/Index.cshtml`**

```cshtml
@model List<Ats.Domain.Entities.PipelineTemplate>
@{ ViewData["Title"] = "Pipelines"; }
<div class="mb-3"><a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New pipeline</a></div>
<table class="table table-hover bg-white">
    <thead><tr><th>Name</th><th>Stages</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var t in Model)
    {
        <tr>
            <td>@t.Name</td>
            <td>@t.Stages.Count</td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@t.Id">Edit</a>
                <form asp-action="Delete" asp-route-id="@t.Id" method="post" class="d-inline"
                      onsubmit="return confirm('Delete this pipeline?');">
                    <button class="btn btn-sm btn-outline-danger" type="submit">Delete</button>
                </form>
            </td>
        </tr>
    }
    @if (Model.Count == 0) { <tr><td colspan="3" class="text-muted">No pipelines yet.</td></tr> }
    </tbody>
</table>
```

- [ ] **Step 4: Create `Views/Pipelines/Form.cshtml`** (stage rows edited as an indexed collection; add via a JS-cloned template row; remove via a hidden Delete flag; order via a numeric field)

```cshtml
@model Ats.Web.Models.PipelineEditViewModel
@using Ats.Domain.Entities
@{ ViewData["Title"] = Model.Id is null ? "New pipeline" : "Edit pipeline"; }
<form asp-action="Save" method="post" class="col-lg-9">
    <div asp-validation-summary="All" class="text-danger small mb-2"></div>
    <input type="hidden" asp-for="Id" />
    <div class="mb-3">
        <label asp-for="Name" class="form-label">Pipeline name</label>
        <input asp-for="Name" class="form-control" />
    </div>

    <h2 class="h5">Stages</h2>
    <table class="table align-middle bg-white" id="stages">
        <thead><tr><th style="width:5rem">Order</th><th>Name</th><th>Terminal</th><th>Outcome</th><th>Status override</th><th></th></tr></thead>
        <tbody>
        @for (var i = 0; i < Model.Stages.Count; i++)
        {
            <tr>
                <td>
                    <input type="hidden" asp-for="Stages[i].Id" />
                    <input type="hidden" asp-for="Stages[i].Delete" class="delete-flag" />
                    <input asp-for="Stages[i].Order" class="form-control form-control-sm" type="number" />
                </td>
                <td><input asp-for="Stages[i].Name" class="form-control form-control-sm" /></td>
                <td class="text-center"><input asp-for="Stages[i].IsTerminal" class="form-check-input" type="checkbox" /></td>
                <td>
                    <select asp-for="Stages[i].TerminalOutcome" asp-items="Html.GetEnumSelectList<StageOutcome>()" class="form-select form-select-sm"></select>
                </td>
                <td><input asp-for="Stages[i].ReferralStatusOverride" class="form-control form-control-sm" placeholder="(defaults to name)" /></td>
                <td><button type="button" class="btn btn-sm btn-outline-danger remove-row">Remove</button></td>
            </tr>
        }
        </tbody>
    </table>
    <button type="button" class="btn btn-outline-secondary btn-sm mb-3" id="add-stage"><i class="bi bi-plus-lg"></i> Add stage</button>
    <div>
        <button type="submit" class="btn btn-primary">Save</button>
        <a class="btn btn-link" asp-action="Index">Cancel</a>
    </div>
</form>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
    <script>
        (function () {
            const tbody = document.querySelector('#stages tbody');
            function reindex() {
                tbody.querySelectorAll('tr').forEach((tr, i) => {
                    tr.querySelectorAll('input,select').forEach(el => {
                        if (el.name) el.name = el.name.replace(/Stages\[\d+\]/, 'Stages[' + i + ']');
                        if (el.id) el.id = el.id.replace(/Stages_\d+__/, 'Stages_' + i + '__');
                    });
                });
            }
            document.querySelector('#add-stage').addEventListener('click', function () {
                const rows = tbody.querySelectorAll('tr');
                const i = rows.length;
                const tpl = rows.length ? rows[rows.length - 1].cloneNode(true) : null;
                if (!tpl) return;
                tpl.querySelectorAll('input').forEach(el => {
                    if (el.type === 'checkbox') el.checked = false;
                    else if (!el.classList.contains('delete-flag')) el.value = '';
                });
                // new row has no Id
                const idHidden = tpl.querySelector('input[name$=".Id"]');
                if (idHidden) idHidden.value = '';
                tpl.querySelector('.delete-flag').value = 'false';
                tbody.appendChild(tpl);
                reindex();
            });
            tbody.addEventListener('click', function (e) {
                if (!e.target.classList.contains('remove-row')) return;
                const tr = e.target.closest('tr');
                const idHidden = tr.querySelector('input[name$=".Id"]');
                if (idHidden && idHidden.value) {
                    // existing stage: mark deleted and hide
                    tr.querySelector('.delete-flag').value = 'true';
                    tr.style.display = 'none';
                } else {
                    tr.remove();
                    reindex();
                }
            });
        })();
    </script>
}
```

- [ ] **Step 5: Build and run**

Run: `dotnet build` (expected 0 errors), then `dotnet run --project src/Ats.Web` and sign in.
Browse `/Pipelines`: create a template with several stages (mark the last terminal with outcome Hired),
save, edit it (add a stage, remove a stage, reorder), and confirm it persists. Stop the app.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): pipeline template editor with stage management"
```

---

## Task 9: Job service + repository + DI

**Files:**
- Create: `src/Ats.Application/Jobs/JobModels.cs`, `IJobRepository.cs`, `JobService.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/JobRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `JobModels.cs`**

```csharp
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

public record JobInput(
    int? Id, string Title, string? Description, int? DepartmentId, int? LocationId,
    EmploymentType EmploymentType, int PipelineTemplateId);
```

- [ ] **Step 2: Create `IJobRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Jobs;

public interface IJobRepository
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task<int> NextJobNumberAsync(CancellationToken ct = default);
    Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `JobService.cs`**

```csharp
using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Jobs;

public interface IJobService
{
    Task<List<Job>> ListAsync(CancellationToken ct = default);
    Task<Job?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(JobInput input, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(JobInput input, CancellationToken ct = default);
    Task<OperationResult> PublishAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CloseAsync(int id, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class JobService : IJobService
{
    private readonly IJobRepository _repo;
    public JobService(IJobRepository repo) => _repo = repo;

    public Task<List<Job>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Job?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(JobInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return OperationResult.Fail("Title is required.");
        if (!await _repo.PipelineExistsAsync(input.PipelineTemplateId, ct))
            return OperationResult.Fail("Select a valid pipeline template.");

        var number = await _repo.NextJobNumberAsync(ct);
        var job = new Job
        {
            Title = input.Title.Trim(),
            Description = input.Description,
            DepartmentId = input.DepartmentId,
            LocationId = input.LocationId,
            EmploymentType = input.EmploymentType,
            PipelineTemplateId = input.PipelineTemplateId,
            Status = JobStatus.Draft,
            ExternalRef = $"JOB-{number}"
        };
        await _repo.AddAsync(job, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(JobInput input, CancellationToken ct = default)
    {
        if (input.Id is not int id) return OperationResult.Fail("Missing job id.");
        if (string.IsNullOrWhiteSpace(input.Title)) return OperationResult.Fail("Title is required.");
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (!await _repo.PipelineExistsAsync(input.PipelineTemplateId, ct))
            return OperationResult.Fail("Select a valid pipeline template.");

        job.Title = input.Title.Trim();
        job.Description = input.Description;
        job.DepartmentId = input.DepartmentId;
        job.LocationId = input.LocationId;
        job.EmploymentType = input.EmploymentType;
        job.PipelineTemplateId = input.PipelineTemplateId;
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> PublishAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (job.Status == JobStatus.Published) return OperationResult.Fail("Job is already published.");
        job.Status = JobStatus.Published;
        job.PublishedAt ??= DateTimeOffset.UtcNow;
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> CloseAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        if (job.Status != JobStatus.Published) return OperationResult.Fail("Only a published job can be closed.");
        job.Status = JobStatus.Closed;
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var job = await _repo.GetAsync(id, ct);
        if (job is null) return OperationResult.Fail("Job not found.");
        job.IsDeleted = true;   // soft delete
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
```

- [ ] **Step 4: Create `JobRepository.cs`** (ExternalRef counter increment in a transaction)

```csharp
using Ats.Application.Jobs;
using Ats.Domain.Entities;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class JobRepository : IJobRepository
{
    private readonly AtsDbContext _db;
    public JobRepository(AtsDbContext db) => _db = db;

    public Task<List<Job>> ListAsync(CancellationToken ct = default) =>
        _db.Jobs.OrderByDescending(j => j.Id).ToListAsync(ct);

    public Task<Job?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task AddAsync(Job job, CancellationToken ct = default) =>
        await _db.Jobs.AddAsync(job, ct);

    // Increments the current tenant's LastJobNumber and returns the new value.
    public async Task<int> NextJobNumberAsync(CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstAsync(ct); // tenant-filtered to the current tenant
        settings.LastJobNumber += 1;
        return settings.LastJobNumber;
    }

    public Task<bool> PipelineExistsAsync(int pipelineTemplateId, CancellationToken ct = default) =>
        _db.PipelineTemplates.AnyAsync(t => t.Id == pipelineTemplateId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

> The counter increment and the job insert are saved in the same `SaveChangesAsync` call (one
> transaction), so a failure rolls back both. The unique `(TenantId, ExternalRef)` index is the safety
> net against any rare race.

- [ ] **Step 5: Register in `DependencyInjection.cs`**

Add before `return services;`:

```csharp
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IJobService, JobService>();
```

Add `using Ats.Application.Jobs;` at the top.

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat: job service with ExternalRef generation and lifecycle"
```

---

## Task 10: Jobs controller + views

**Files:**
- Create: `src/Ats.Web/Models/JobEditViewModel.cs`.
- Create: `src/Ats.Web/Controllers/JobsController.cs`.
- Create: `src/Ats.Web/Views/Jobs/Index.cshtml`, `Form.cshtml`.

- [ ] **Step 1: Create `JobEditViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Models;

public class JobEditViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    [Required] public int PipelineTemplateId { get; set; }

    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Locations { get; set; } = new();
    public List<SelectListItem> Pipelines { get; set; } = new();
}
```

- [ ] **Step 2: Create `JobsController.cs`**

```csharp
using Ats.Application.Departments;
using Ats.Application.Jobs;
using Ats.Application.Locations;
using Ats.Application.Pipelines;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.Controllers;

[Authorize]
public class JobsController : Controller
{
    private readonly IJobService _jobs;
    private readonly IDepartmentService _departments;
    private readonly ILocationService _locations;
    private readonly IPipelineTemplateService _pipelines;

    public JobsController(IJobService jobs, IDepartmentService departments,
        ILocationService locations, IPipelineTemplateService pipelines)
    {
        _jobs = jobs; _departments = departments; _locations = locations; _pipelines = pipelines;
    }

    public async Task<IActionResult> Index() => View(await _jobs.ListAsync());

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new JobEditViewModel();
        await PopulateLists(vm);
        return View("Form", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Create(JobEditViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLists(vm); return View("Form", vm); }
        var result = await _jobs.CreateAsync(new JobInput(null, vm.Title, vm.Description,
            vm.DepartmentId, vm.LocationId, vm.EmploymentType, vm.PipelineTemplateId));
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); await PopulateLists(vm); return View("Form", vm); }
        TempData["Success"] = "Job created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var job = await _jobs.GetAsync(id);
        if (job is null) return NotFound();
        var vm = new JobEditViewModel
        {
            Id = job.Id, Title = job.Title, Description = job.Description,
            DepartmentId = job.DepartmentId, LocationId = job.LocationId,
            EmploymentType = job.EmploymentType, PipelineTemplateId = job.PipelineTemplateId
        };
        await PopulateLists(vm);
        return View("Form", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(JobEditViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateLists(vm); return View("Form", vm); }
        var result = await _jobs.UpdateAsync(new JobInput(vm.Id, vm.Title, vm.Description,
            vm.DepartmentId, vm.LocationId, vm.EmploymentType, vm.PipelineTemplateId));
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); await PopulateLists(vm); return View("Form", vm); }
        TempData["Success"] = "Job updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost] public Task<IActionResult> Publish(int id) => Lifecycle(_jobs.PublishAsync(id), "Job published.");
    [HttpPost] public Task<IActionResult> Close(int id) => Lifecycle(_jobs.CloseAsync(id), "Job closed.");

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _jobs.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Job deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Lifecycle(Task<OperationResult> action, string okMessage)
    {
        var result = await action;
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? okMessage : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateLists(JobEditViewModel vm)
    {
        vm.Departments = (await _departments.ListAsync())
            .Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToList();
        vm.Locations = (await _locations.ListAsync())
            .Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToList();
        vm.Pipelines = (await _pipelines.ListAsync())
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();
    }
}
```

- [ ] **Step 3: Create `Views/Jobs/Index.cshtml`**

```cshtml
@model List<Ats.Domain.Entities.Job>
@using Ats.Domain.Enums
@{ ViewData["Title"] = "Jobs"; }
<div class="mb-3"><a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New job</a></div>
<table class="table table-hover bg-white">
    <thead><tr><th>Ref</th><th>Title</th><th>Status</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var j in Model)
    {
        <tr>
            <td><code>@j.ExternalRef</code></td>
            <td>@j.Title</td>
            <td>
                @{
                    var badge = j.Status switch {
                        JobStatus.Published => "bg-success",
                        JobStatus.Closed => "bg-secondary",
                        _ => "bg-warning text-dark"
                    };
                }
                <span class="badge @badge">@j.Status</span>
            </td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@j.Id">Edit</a>
                @if (j.Status != JobStatus.Published)
                {
                    <form asp-action="Publish" asp-route-id="@j.Id" method="post" class="d-inline">
                        <button class="btn btn-sm btn-outline-success" type="submit">Publish</button>
                    </form>
                }
                @if (j.Status == JobStatus.Published)
                {
                    <form asp-action="Close" asp-route-id="@j.Id" method="post" class="d-inline">
                        <button class="btn btn-sm btn-outline-secondary" type="submit">Close</button>
                    </form>
                }
                <form asp-action="Delete" asp-route-id="@j.Id" method="post" class="d-inline"
                      onsubmit="return confirm('Delete this job?');">
                    <button class="btn btn-sm btn-outline-danger" type="submit">Delete</button>
                </form>
            </td>
        </tr>
    }
    @if (Model.Count == 0) { <tr><td colspan="4" class="text-muted">No jobs yet.</td></tr> }
    </tbody>
</table>
```

- [ ] **Step 4: Create `Views/Jobs/Form.cshtml`**

```cshtml
@model Ats.Web.Models.JobEditViewModel
@using Ats.Domain.Enums
@{ ViewData["Title"] = Model.Id is null ? "New job" : "Edit job"; }
<form asp-action="@(Model.Id is null ? "Create" : "Edit")" method="post" class="col-lg-8">
    <div asp-validation-summary="All" class="text-danger small mb-2"></div>
    <input type="hidden" asp-for="Id" />
    <div class="mb-3">
        <label asp-for="Title" class="form-label">Title</label>
        <input asp-for="Title" class="form-control" />
        <span asp-validation-for="Title" class="text-danger small"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Description" class="form-label">Description</label>
        <textarea asp-for="Description" class="form-control" rows="5"></textarea>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="DepartmentId" class="form-label">Department</label>
            <select asp-for="DepartmentId" asp-items="Model.Departments" class="form-select"><option value="">(none)</option></select>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="LocationId" class="form-label">Location</label>
            <select asp-for="LocationId" asp-items="Model.Locations" class="form-select"><option value="">(none)</option></select>
        </div>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="EmploymentType" class="form-label">Employment type</label>
            <select asp-for="EmploymentType" asp-items="Html.GetEnumSelectList<EmploymentType>()" class="form-select"></select>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="PipelineTemplateId" class="form-label">Pipeline</label>
            <select asp-for="PipelineTemplateId" asp-items="Model.Pipelines" class="form-select"><option value="">Select a pipeline</option></select>
            <span asp-validation-for="PipelineTemplateId" class="text-danger small"></span>
        </div>
    </div>
    <button type="submit" class="btn btn-primary">Save</button>
    <a class="btn btn-link" asp-action="Index">Cancel</a>
</form>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 5: Build and run**

Run: `dotnet build` (expected 0 errors), then `dotnet run --project src/Ats.Web` and sign in.
Browse `/Jobs`: create a job (pick the pipeline created earlier), confirm it gets `JOB-1`; publish it
(status badge turns green, `PublishedAt` set); close it; create a second job and confirm `JOB-2`.
Stop the app.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): jobs CRUD and publish/close lifecycle"
```

---

## Task 11: Sidebar navigation entries

**Files:**
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`.

- [ ] **Step 1: Add the Plan A nav items**

Replace the `Items` array with:

```csharp
    private static readonly NavItem[] Items =
    {
        new("Dashboard", "bi-speedometer2", "Dashboard", "Index"),
        new("Jobs", "bi-briefcase", "Jobs", "Index"),
        new("Pipelines", "bi-diagram-3", "Pipelines", "Index"),
        new("Departments", "bi-building", "Departments", "Index"),
        new("Locations", "bi-geo-alt", "Locations", "Index"),
    };
```

- [ ] **Step 2: Build and run**

Run: `dotnet build` (expected 0 errors), then `dotnet run --project src/Ats.Web` and sign in.
Confirm the sidebar lists Dashboard, Jobs, Pipelines, Departments, Locations, and that the active item
highlights as you navigate. Stop the app.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): add Plan A sidebar navigation"
```

---

## Task 12: Manual end-to-end verification of Plan A

**No new files.**

- [ ] **Step 1: Run the app and sign in** (`dotnet run --project src/Ats.Web`, https).

- [ ] **Step 2: Pipeline** - create a "Standard hiring" template with stages Applied, 1st Interview, Hired (terminal, outcome Hired). Save and re-open to confirm persistence and order.

- [ ] **Step 3: Lookups** - create a department and a location; edit each; confirm delete is blocked once a job references them.

- [ ] **Step 4: Job lifecycle** - create a job using the pipeline; confirm `JOB-1`; publish (badge green, `PublishedAt` set in SQL); close; create a second job to confirm `JOB-2`; soft-delete a draft and confirm it disappears from the list but the next job is `JOB-3` (no reuse).

- [ ] **Step 5: Tenancy** - sign in as a second tenant's owner and confirm none of tenant 1's jobs, pipelines, departments, or locations are visible.

- [ ] **Step 6: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 1 Plan A complete and verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage (Plan A scope):** soft delete + filter (Task 1); enums + ExternalRef counter (Task 2); Department/Location/Job entities + configs + DbSets (Task 3); migration created, developer applies (Task 4); Department/Location services + repos + DI (Task 5) and screens (Task 6); pipeline template service with full stage CRUD (Task 7) and editor UI (Task 8); job service with ExternalRef generation + Draft/Published/Closed lifecycle (Task 9) and screens (Task 10); sidebar entries (Task 11); verification incl. cross-tenant check and no-ExternalRef-reuse (Task 12).
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion. The one prose note in Task 5 Step 2 is immediately followed by the corrected `DeleteAsync` body and the `RemoveAsync` interface member in Step 3, so no dangling reference remains.
- **Type consistency:** `OperationResult` defined once in `Ats.Application/Departments/DepartmentService.cs` and reused by Location/Pipeline/Job services via `using Ats.Application.Departments;`. `JobInput`, `PipelineTemplateInput`/`StageInput`, repository method names (`NextJobNumberAsync`, `IsReferencedByJobAsync`, `IsUsedByJobAsync`, `RemoveAsync`) match between interface, implementation, and caller. `StageOutcome` reused from Phase 0.
- **Soft delete scope:** only `Job` implements `ISoftDeletable` in Plan A (Candidate/Application in Plan B). Departments/Locations are hard-deleted with an in-use guard, matching the spec's soft-delete list.
- **Ordering:** every task builds green on its own; the migration (Task 4) follows the entities (Tasks 1-3) and precedes the services/UI that depend only on code, not schema.
```
