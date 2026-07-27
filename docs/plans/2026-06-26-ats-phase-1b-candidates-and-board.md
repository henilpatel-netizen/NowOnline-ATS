# ATS Phase 1 - Plan B: Candidates and Board Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a recruiter add candidates to a published job and move them through pipeline stages on a kanban board (drag-and-drop with an accessible fallback), with optimistic-concurrency-safe moves and a full ordered stage-move history.

**Architecture:** Adds Candidate, Application (with RowVersion), and ApplicationEvent entities and one migration; Application services with repository interfaces (Infrastructure implementations, matching the Phase 0/Plan A pattern); a candidates CRUD area; and a per-job board rendered server-side, with SortableJS + htmx posting stage moves to a server endpoint that re-renders the board partial. Concurrency is enforced with a SQL rowversion.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, SQL Server, Bootstrap 5, htmx, SortableJS.

**Reference spec:** `docs/specs/2026-06-26-ats-phase-1-core-ats-design.md` (this is Plan B of two; Plan A is complete).

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`; UI tasks add a manual run check (the developer runs in-app checks after the phase).
- **Commits are manual.** The developer runs every `git commit`.
- **Migrations:** created with `dotnet ef migrations add` (allowed), applied by the developer with `database update`.
- **Working directory** is `D:\LiveProject\Ats`.
- **No em dashes and no emoji** in generated files.
- New services and repositories are registered in `Ats.Infrastructure/DependencyInjection.cs`.
- `ISoftDeletable` and `OperationResult` already exist from Plan A and are reused.

---

## File structure (created or modified by Plan B)

```
src\Ats.Domain\
  Enums\ApplicationStatus.cs                         # NEW
  Entities\Candidate.cs                              # NEW
  Entities\Application.cs                            # NEW
  Entities\ApplicationEvent.cs                       # NEW
src\Ats.Infrastructure\
  Persistence\AtsDbContext.cs                        # MODIFY: DbSets
  Persistence\Configurations\CandidateConfiguration.cs       # NEW
  Persistence\Configurations\ApplicationConfiguration.cs     # NEW
  Persistence\Configurations\ApplicationEventConfiguration.cs# NEW
  Persistence\Repositories\CandidateRepository.cs    # NEW
  Persistence\Repositories\ApplicationRepository.cs  # NEW
  Migrations\*_AddCandidateApplication.cs            # NEW (generated)
  DependencyInjection.cs                             # MODIFY
src\Ats.Application\
  Candidates\ICandidateRepository.cs, CandidateService.cs    # NEW
  Applications\IApplicationRepository.cs, ApplicationService.cs, ApplicationModels.cs  # NEW
src\Ats.Web\
  Models\CandidateViewModel.cs, BoardViewModel.cs, AddCandidateViewModel.cs  # NEW
  Controllers\CandidatesController.cs, BoardController.cs, ApplicationsController.cs  # NEW
  Views\Candidates\Index.cshtml, Form.cshtml         # NEW
  Views\Board\Index.cshtml, _Board.cshtml            # NEW
  Views\Applications\Details.cshtml                  # NEW
  ViewComponents\SidebarNavViewComponent.cs          # MODIFY: Candidates entry
```

---

## Task 1: Candidate, Application, ApplicationEvent entities + enum + configs + DbSets

**Files:**
- Create: `src/Ats.Domain/Enums/ApplicationStatus.cs`, `Entities/Candidate.cs`, `Entities/Application.cs`, `Entities/ApplicationEvent.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Configurations/CandidateConfiguration.cs`, `ApplicationConfiguration.cs`, `ApplicationEventConfiguration.cs`.
- Modify: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs`.

- [ ] **Step 1: Create `ApplicationStatus.cs`**

```csharp
namespace Ats.Domain.Enums;

public enum ApplicationStatus
{
    Active = 0,
    Hired = 1,
    Rejected = 2,
    Withdrawn = 3
}
```

- [ ] **Step 2: Create `Candidate.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class Candidate : TenantEntity, ISoftDeletable
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ResumeFileKey { get; set; }   // set in Phase 2
    public bool IsDeleted { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
```

- [ ] **Step 3: Create `Application.cs`**

```csharp
using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class Application : TenantEntity, ISoftDeletable
{
    public int CandidateId { get; set; }
    public int JobId { get; set; }
    public int CurrentStageId { get; set; }
    public string? SourceCode { get; set; }   // captured in Phase 2
    public DateTimeOffset AppliedAt { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Active;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public bool IsDeleted { get; set; }

    public Candidate? Candidate { get; set; }
}
```

- [ ] **Step 4: Create `ApplicationEvent.cs`**

```csharp
using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class ApplicationEvent : TenantEntity
{
    public int ApplicationId { get; set; }
    public int? FromStageId { get; set; }
    public int ToStageId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? MovedByUserId { get; set; }
}
```

- [ ] **Step 5: Create `CandidateConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> b)
    {
        b.HasKey(c => c.Id);
        b.Ignore(c => c.FullName);
        b.Property(c => c.FirstName).IsRequired().HasMaxLength(100);
        b.Property(c => c.LastName).IsRequired().HasMaxLength(100);
        b.Property(c => c.Email).IsRequired().HasMaxLength(256);
        b.Property(c => c.Phone).HasMaxLength(40);
        b.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
    }
}
```

- [ ] **Step 6: Create `ApplicationConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.SourceCode).HasMaxLength(36);
        b.Property(a => a.RowVersion).IsRowVersion();
        b.HasIndex(a => new { a.TenantId, a.JobId, a.CandidateId }).IsUnique();
        b.HasOne(a => a.Candidate).WithMany().HasForeignKey(a => a.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Job>().WithMany().HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PipelineStage>().WithMany().HasForeignKey(a => a.CurrentStageId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 7: Create `ApplicationEventConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class ApplicationEventConfiguration : IEntityTypeConfiguration<ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ApplicationEvent> b)
    {
        b.HasKey(e => e.Id);
        b.HasIndex(e => new { e.TenantId, e.ApplicationId, e.OccurredAt });
        b.HasOne<Application>().WithMany().HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 8: Add DbSets in `AtsDbContext`**

Add next to the existing DbSets:

```csharp
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();
```

- [ ] **Step 9: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit** (developer)

```bash
git add -A
git commit -m "feat(domain): add candidate, application, application-event entities"
```

---

## Task 2: Migration for Plan B schema

**Files:**
- Create: `src/Ats.Infrastructure/Migrations/*_AddCandidateApplication.cs` (generated).

- [ ] **Step 1: Create the migration**

```bash
cd /d/LiveProject/Ats
dotnet ef migrations add AddCandidateApplication --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```
Expected: a migration creating `Candidates`, `Applications` (with a `rowversion` column and unique
`(TenantId, JobId, CandidateId)` index), and `ApplicationEvents`.

- [ ] **Step 2: Sanity-check**

```bash
grep -hoE "CreateTable\(|name: \"(Candidates|Applications|ApplicationEvents)\"|rowversion|RowVersion" src/Ats.Infrastructure/Migrations/*_AddCandidateApplication.cs | sort -u
```
Expected: the three tables and a RowVersion/rowversion column.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Developer applies the migration** (the AI must NOT)

```bash
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): migration for candidates and applications"
```

---

## Task 3: Candidate service + repository + DI

**Files:**
- Create: `src/Ats.Application/Candidates/ICandidateRepository.cs`, `CandidateService.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/CandidateRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `ICandidateRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Candidates;

public interface ICandidateRepository
{
    Task<List<Candidate>> ListAsync(CancellationToken ct = default);
    Task<Candidate?> GetAsync(int id, CancellationToken ct = default);
    Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(Candidate candidate, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `CandidateService.cs`**

```csharp
using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;

namespace Ats.Application.Candidates;

public interface ICandidateService
{
    Task<List<Candidate>> ListAsync(CancellationToken ct = default);
    Task<Candidate?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> CreateAsync(string firstName, string lastName, string email, string? phone, CancellationToken ct = default);
    Task<OperationResult> UpdateAsync(int id, string firstName, string lastName, string email, string? phone, CancellationToken ct = default);
}

public sealed class CandidateService : ICandidateService
{
    private readonly ICandidateRepository _repo;
    public CandidateService(ICandidateRepository repo) => _repo = repo;

    public Task<List<Candidate>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Candidate?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

    public async Task<OperationResult> CreateAsync(string firstName, string lastName, string email, string? phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return OperationResult.Fail("Email is required.");
        var normalized = email.Trim().ToLowerInvariant();
        if (await _repo.GetByEmailAsync(normalized, ct) is not null)
            return OperationResult.Fail("A candidate with this email already exists.");
        await _repo.AddAsync(new Candidate
        {
            FirstName = firstName.Trim(), LastName = lastName.Trim(),
            Email = normalized, Phone = phone?.Trim()
        }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UpdateAsync(int id, string firstName, string lastName, string email, string? phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return OperationResult.Fail("Email is required.");
        var candidate = await _repo.GetAsync(id, ct);
        if (candidate is null) return OperationResult.Fail("Candidate not found.");
        var normalized = email.Trim().ToLowerInvariant();
        var byEmail = await _repo.GetByEmailAsync(normalized, ct);
        if (byEmail is not null && byEmail.Id != id)
            return OperationResult.Fail("Another candidate already uses this email.");
        candidate.FirstName = firstName.Trim();
        candidate.LastName = lastName.Trim();
        candidate.Email = normalized;
        candidate.Phone = phone?.Trim();
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }
}
```

- [ ] **Step 3: Create `CandidateRepository.cs`**

```csharp
using Ats.Application.Candidates;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class CandidateRepository : ICandidateRepository
{
    private readonly AtsDbContext _db;
    public CandidateRepository(AtsDbContext db) => _db = db;

    public Task<List<Candidate>> ListAsync(CancellationToken ct = default) =>
        _db.Candidates.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync(ct);

    public Task<Candidate?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Candidates.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Candidates.FirstOrDefaultAsync(c => c.Email == email, ct);

    public async Task AddAsync(Candidate candidate, CancellationToken ct = default) =>
        await _db.Candidates.AddAsync(candidate, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Register in `DependencyInjection.cs`**

Add `using Ats.Application.Candidates;` at the top, and before `return services;`:

```csharp
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<ICandidateService, CandidateService>();
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat: candidate service with repository"
```

---

## Task 4: Candidates controller + views + sidebar entry

**Files:**
- Create: `src/Ats.Web/Models/CandidateViewModel.cs`.
- Create: `src/Ats.Web/Controllers/CandidatesController.cs`.
- Create: `src/Ats.Web/Views/Candidates/Index.cshtml`, `Form.cshtml`.
- Modify: `src/Ats.Web/ViewComponents/SidebarNavViewComponent.cs`.

- [ ] **Step 1: Create `CandidateViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class CandidateViewModel
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [StringLength(40)] public string? Phone { get; set; }
}
```

- [ ] **Step 2: Create `CandidatesController.cs`**

```csharp
using Ats.Application.Candidates;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class CandidatesController : Controller
{
    private readonly ICandidateService _service;
    public CandidatesController(ICandidateService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.ListAsync());

    [HttpGet] public IActionResult Create() => View("Form", new CandidateViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CandidateViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.CreateAsync(vm.FirstName, vm.LastName, vm.Email, vm.Phone);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Candidate created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await _service.GetAsync(id);
        if (c is null) return NotFound();
        return View("Form", new CandidateViewModel { Id = c.Id, FirstName = c.FirstName, LastName = c.LastName, Email = c.Email, Phone = c.Phone });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(CandidateViewModel vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var result = await _service.UpdateAsync(vm.Id, vm.FirstName, vm.LastName, vm.Email, vm.Phone);
        if (!result.Succeeded) { ModelState.AddModelError("", result.Error!); return View("Form", vm); }
        TempData["Success"] = "Candidate updated.";
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 3: Create `Views/Candidates/Index.cshtml`**

```cshtml
@model List<Ats.Domain.Entities.Candidate>
@{ ViewData["Title"] = "Candidates"; }
<div class="mb-3"><a class="btn btn-primary" asp-action="Create"><i class="bi bi-plus-lg"></i> New candidate</a></div>
<table class="table table-hover bg-white">
    <thead><tr><th>Name</th><th>Email</th><th>Phone</th><th class="text-end">Actions</th></tr></thead>
    <tbody>
    @foreach (var c in Model)
    {
        <tr>
            <td>@c.FullName</td>
            <td>@c.Email</td>
            <td>@c.Phone</td>
            <td class="text-end">
                <a class="btn btn-sm btn-outline-secondary" asp-action="Edit" asp-route-id="@c.Id">Edit</a>
            </td>
        </tr>
    }
    @if (Model.Count == 0) { <tr><td colspan="4" class="text-muted">No candidates yet.</td></tr> }
    </tbody>
</table>
```

- [ ] **Step 4: Create `Views/Candidates/Form.cshtml`**

```cshtml
@model Ats.Web.Models.CandidateViewModel
@{ ViewData["Title"] = Model.Id == 0 ? "New candidate" : "Edit candidate"; }
<form asp-action="@(Model.Id == 0 ? "Create" : "Edit")" method="post" class="col-md-6">
    <div asp-validation-summary="All" class="text-danger small mb-2"></div>
    <input type="hidden" asp-for="Id" />
    <div class="mb-3"><label asp-for="FirstName" class="form-label">First name</label>
        <input asp-for="FirstName" class="form-control" /><span asp-validation-for="FirstName" class="text-danger small"></span></div>
    <div class="mb-3"><label asp-for="LastName" class="form-label">Last name</label>
        <input asp-for="LastName" class="form-control" /><span asp-validation-for="LastName" class="text-danger small"></span></div>
    <div class="mb-3"><label asp-for="Email" class="form-label">Email</label>
        <input asp-for="Email" class="form-control" /><span asp-validation-for="Email" class="text-danger small"></span></div>
    <div class="mb-3"><label asp-for="Phone" class="form-label">Phone</label>
        <input asp-for="Phone" class="form-control" /><span asp-validation-for="Phone" class="text-danger small"></span></div>
    <button type="submit" class="btn btn-primary">Save</button>
    <a class="btn btn-link" asp-action="Index">Cancel</a>
</form>
@section Scripts { <partial name="_ValidationScriptsPartial" /> }
```

- [ ] **Step 5: Add the Candidates sidebar entry**

In `SidebarNavViewComponent.cs`, insert after the Pipelines item:

```csharp
        new("Candidates", "bi-people", "Candidates", "Index"),
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): candidates CRUD and sidebar entry"
```

---

## Task 5: Application service + repository + DI

**Files:**
- Create: `src/Ats.Application/Applications/ApplicationModels.cs`, `IApplicationRepository.cs`, `ApplicationService.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/ApplicationRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `ApplicationModels.cs`**

```csharp
namespace Ats.Application.Applications;

public record AddCandidateToJobInput(
    int JobId, string FirstName, string LastName, string Email, string? Phone);
```

- [ ] **Step 2: Create `IApplicationRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Applications;

public interface IApplicationRepository
{
    Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default);
    Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default);
    Task<List<Application>> ListForJobAsync(int jobId, CancellationToken ct = default);
    Task<Application?> GetAsync(int id, CancellationToken ct = default);
    Task<Application?> FindByCandidateJobAsync(int candidateId, int jobId, CancellationToken ct = default);
    Task AddApplicationAsync(Application application, CancellationToken ct = default);
    Task AddEventAsync(ApplicationEvent ev, CancellationToken ct = default);
    Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default);
    void SetExpectedRowVersion(Application application, byte[] rowVersion);
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default); // false on concurrency conflict
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `ApplicationService.cs`**

```csharp
using Ats.Application.Abstractions;
using Ats.Application.Candidates;
using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Applications;

public interface IApplicationService
{
    Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default);
    Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default);
    Task<List<Application>> ListForJobAsync(int jobId, CancellationToken ct = default);
    Task<Application?> GetAsync(int id, CancellationToken ct = default);
    Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default);
    Task<OperationResult> AddCandidateToJobAsync(AddCandidateToJobInput input, CancellationToken ct = default);
    Task<OperationResult> MoveStageAsync(int applicationId, int toStageId, byte[] rowVersion, CancellationToken ct = default);
}

public sealed class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _repo;
    private readonly ICandidateRepository _candidates;
    private readonly ICurrentUser _currentUser;

    public ApplicationService(IApplicationRepository repo, ICandidateRepository candidates, ICurrentUser currentUser)
    {
        _repo = repo; _candidates = candidates; _currentUser = currentUser;
    }

    public Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default) => _repo.GetJobAsync(jobId, ct);
    public Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default) => _repo.GetStagesForJobAsync(jobId, ct);
    public Task<List<Application>> ListForJobAsync(int jobId, CancellationToken ct = default) => _repo.ListForJobAsync(jobId, ct);
    public Task<Application?> GetAsync(int id, CancellationToken ct = default) => _repo.GetAsync(id, ct);
    public Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default) => _repo.ListEventsAsync(applicationId, ct);

    public async Task<OperationResult> AddCandidateToJobAsync(AddCandidateToJobInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Email)) return OperationResult.Fail("Email is required.");
        var job = await _repo.GetJobAsync(input.JobId, ct);
        if (job is null) return OperationResult.Fail("Job not found.");

        var stages = await _repo.GetStagesForJobAsync(input.JobId, ct);
        var firstStage = stages.OrderBy(s => s.Order).FirstOrDefault();
        if (firstStage is null) return OperationResult.Fail("This job's pipeline has no stages.");

        var email = input.Email.Trim().ToLowerInvariant();
        var candidate = await _candidates.GetByEmailAsync(email, ct);
        if (candidate is null)
        {
            candidate = new Candidate
            {
                FirstName = input.FirstName.Trim(), LastName = input.LastName.Trim(),
                Email = email, Phone = input.Phone?.Trim()
            };
            await _candidates.AddAsync(candidate, ct);
            await _candidates.SaveChangesAsync(ct);   // assigns candidate.Id
        }

        var existing = await _repo.FindByCandidateJobAsync(candidate.Id, input.JobId, ct);
        if (existing is not null) return OperationResult.Ok;  // already applied; no duplicate

        var application = new Application
        {
            CandidateId = candidate.Id,
            JobId = input.JobId,
            CurrentStageId = firstStage.Id,
            AppliedAt = DateTimeOffset.UtcNow,
            Status = ApplicationStatus.Active
        };
        await _repo.AddApplicationAsync(application, ct);
        await _repo.SaveChangesAsync(ct);   // assigns application.Id

        await _repo.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = _currentUser.UserId
        }, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> MoveStageAsync(int applicationId, int toStageId, byte[] rowVersion, CancellationToken ct = default)
    {
        var application = await _repo.GetAsync(applicationId, ct);
        if (application is null) return OperationResult.Fail("Application not found.");

        var stages = await _repo.GetStagesForJobAsync(application.JobId, ct);
        var target = stages.FirstOrDefault(s => s.Id == toStageId);
        if (target is null) return OperationResult.Fail("That stage does not belong to this job's pipeline.");
        if (application.CurrentStageId == toStageId) return OperationResult.Ok; // no-op

        var fromStageId = application.CurrentStageId;
        application.CurrentStageId = toStageId;
        application.Status = target.IsTerminal
            ? (target.TerminalOutcome == StageOutcome.Hired ? ApplicationStatus.Hired
               : target.TerminalOutcome == StageOutcome.Rejected ? ApplicationStatus.Rejected
               : ApplicationStatus.Active)
            : ApplicationStatus.Active;

        await _repo.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = fromStageId,
            ToStageId = toStageId,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = _currentUser.UserId
        }, ct);

        _repo.SetExpectedRowVersion(application, rowVersion);
        var ok = await _repo.TrySaveChangesAsync(ct);
        return ok ? OperationResult.Ok
                  : OperationResult.Fail("This application was changed by someone else. Reload the board and try again.");
    }
}
```

- [ ] **Step 4: Create `ApplicationRepository.cs`**

```csharp
using Ats.Application.Applications;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class ApplicationRepository : IApplicationRepository
{
    private readonly AtsDbContext _db;
    public ApplicationRepository(AtsDbContext db) => _db = db;

    public Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default) =>
        _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public async Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default)
    {
        var templateId = await _db.Jobs.Where(j => j.Id == jobId)
            .Select(j => (int?)j.PipelineTemplateId).FirstOrDefaultAsync(ct);
        if (templateId is null) return new List<PipelineStage>();
        return await _db.PipelineStages.Where(s => s.PipelineTemplateId == templateId.Value)
            .OrderBy(s => s.Order).ToListAsync(ct);
    }

    public Task<List<Application>> ListForJobAsync(int jobId, CancellationToken ct = default) =>
        _db.Applications.Include(a => a.Candidate)
            .Where(a => a.JobId == jobId)
            .OrderBy(a => a.AppliedAt).ToListAsync(ct);

    public Task<Application?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Application?> FindByCandidateJobAsync(int candidateId, int jobId, CancellationToken ct = default) =>
        _db.Applications.FirstOrDefaultAsync(a => a.CandidateId == candidateId && a.JobId == jobId, ct);

    public async Task AddApplicationAsync(Application application, CancellationToken ct = default) =>
        await _db.Applications.AddAsync(application, ct);

    public async Task AddEventAsync(ApplicationEvent ev, CancellationToken ct = default) =>
        await _db.ApplicationEvents.AddAsync(ev, ct);

    public Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default) =>
        _db.ApplicationEvents.Where(e => e.ApplicationId == applicationId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(ct);

    public void SetExpectedRowVersion(Application application, byte[] rowVersion) =>
        _db.Entry(application).Property(a => a.RowVersion).OriginalValue = rowVersion;

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Register in `DependencyInjection.cs`**

Add `using Ats.Application.Applications;` at the top, and before `return services;`:

```csharp
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IApplicationService, ApplicationService>();
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat: application service with add-to-job and concurrency-safe stage moves"
```

---

## Task 6: Board controller + views (kanban with drag-and-drop + fallback)

**Files:**
- Create: `src/Ats.Web/Models/BoardViewModel.cs`, `AddCandidateViewModel.cs`.
- Create: `src/Ats.Web/Controllers/BoardController.cs`.
- Create: `src/Ats.Web/Views/Board/Index.cshtml`, `_Board.cshtml`.

- [ ] **Step 1: Create `BoardViewModel.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Web.Models;

public record BoardCard(int ApplicationId, string CandidateName, string RowVersion);
public record BoardColumn(PipelineStage Stage, List<BoardCard> Cards);

public class BoardViewModel
{
    public Job Job { get; set; } = default!;
    public List<BoardColumn> Columns { get; set; } = new();
    public string? Error { get; set; }
}
```

- [ ] **Step 2: Create `AddCandidateViewModel.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models;

public class AddCandidateViewModel
{
    [Required] public int JobId { get; set; }
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [StringLength(40)] public string? Phone { get; set; }
}
```

- [ ] **Step 3: Create `BoardController.cs`**

```csharp
using Ats.Application.Applications;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class BoardController : Controller
{
    private readonly IApplicationService _service;
    public BoardController(IApplicationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index(int jobId)
    {
        var model = await BuildBoardAsync(jobId, null);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Move(int jobId, int applicationId, int toStageId, string rowVersion)
    {
        byte[] rv;
        try { rv = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { rv = Array.Empty<byte>(); }

        var result = await _service.MoveStageAsync(applicationId, toStageId, rv);
        var model = await BuildBoardAsync(jobId, result.Succeeded ? null : result.Error);
        if (model is null) return NotFound();

        if (Request.Headers.ContainsKey("HX-Request"))
            return PartialView("_Board", model);

        if (!result.Succeeded) TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Index), new { jobId });
    }

    [HttpPost]
    public async Task<IActionResult> AddCandidate(AddCandidateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "All candidate fields except phone are required.";
            return RedirectToAction(nameof(Index), new { jobId = vm.JobId });
        }
        var result = await _service.AddCandidateToJobAsync(
            new AddCandidateToJobInput(vm.JobId, vm.FirstName, vm.LastName, vm.Email, vm.Phone));
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Candidate added to job." : result.Error;
        return RedirectToAction(nameof(Index), new { jobId = vm.JobId });
    }

    private async Task<BoardViewModel?> BuildBoardAsync(int jobId, string? error)
    {
        var job = await _service.GetJobAsync(jobId);
        if (job is null) return null;
        var stages = await _service.GetStagesForJobAsync(jobId);
        var apps = await _service.ListForJobAsync(jobId);
        var columns = stages.Select(s => new BoardColumn(s,
            apps.Where(a => a.CurrentStageId == s.Id)
                .Select(a => new BoardCard(a.Id,
                    a.Candidate?.FullName ?? "(unknown)",
                    Convert.ToBase64String(a.RowVersion))).ToList())).ToList();
        return new BoardViewModel { Job = job, Columns = columns, Error = error };
    }
}
```

- [ ] **Step 4: Create `Views/Board/_Board.cshtml`** (the swappable board partial)

```cshtml
@model Ats.Web.Models.BoardViewModel
<div id="board-container">
    @if (!string.IsNullOrEmpty(Model.Error))
    {
        <div class="alert alert-warning" role="alert">@Model.Error</div>
    }
    <div class="d-flex gap-3 overflow-auto pb-2">
        @foreach (var col in Model.Columns)
        {
            <div class="board-col card" style="min-width:240px; max-width:260px;" data-stage-id="@col.Stage.Id">
                <div class="card-header py-2 d-flex justify-content-between">
                    <span class="fw-semibold">@col.Stage.Name</span>
                    <span class="badge bg-secondary">@col.Cards.Count</span>
                </div>
                <div class="card-body p-2 board-cards" style="min-height:60px;">
                    @foreach (var card in col.Cards)
                    {
                        <form class="board-card card mb-2 p-2"
                              asp-controller="Board" asp-action="Move" method="post"
                              hx-post="@Url.Action("Move", "Board")" hx-target="#board-container" hx-swap="outerHTML">
                            <input type="hidden" name="jobId" value="@Model.Job.Id" />
                            <input type="hidden" name="applicationId" value="@card.ApplicationId" />
                            <input type="hidden" name="rowVersion" value="@card.RowVersion" />
                            <input type="hidden" name="toStageId" value="@col.Stage.Id" class="to-stage" />
                            <div class="d-flex justify-content-between align-items-start">
                                <a class="text-decoration-none" asp-controller="Applications" asp-action="Details" asp-route-id="@card.ApplicationId">@card.CandidateName</a>
                            </div>
                            <select class="form-select form-select-sm mt-1 move-select" aria-label="Move to stage">
                                @foreach (var s in Model.Columns)
                                {
                                    <option value="@s.Stage.Id" selected="@(s.Stage.Id == col.Stage.Id)">@s.Stage.Name</option>
                                }
                            </select>
                        </form>
                    }
                </div>
            </div>
        }
    </div>
</div>
```

- [ ] **Step 5: Create `Views/Board/Index.cshtml`** (page shell, add-candidate form, SortableJS wiring)

```cshtml
@model Ats.Web.Models.BoardViewModel
@{ ViewData["Title"] = $"Board: {Model.Job.Title}"; }

<div class="mb-3 d-flex gap-2">
    <a class="btn btn-outline-secondary btn-sm" asp-controller="Jobs" asp-action="Index"><i class="bi bi-arrow-left"></i> Jobs</a>
    <button class="btn btn-primary btn-sm" data-bs-toggle="collapse" data-bs-target="#add-candidate"><i class="bi bi-plus-lg"></i> Add candidate</button>
</div>

<div class="collapse mb-3" id="add-candidate">
    <form asp-action="AddCandidate" method="post" class="card card-body">
        <input type="hidden" name="JobId" value="@Model.Job.Id" />
        <div class="row g-2">
            <div class="col-md-3"><input name="FirstName" class="form-control form-control-sm" placeholder="First name" required /></div>
            <div class="col-md-3"><input name="LastName" class="form-control form-control-sm" placeholder="Last name" required /></div>
            <div class="col-md-3"><input name="Email" type="email" class="form-control form-control-sm" placeholder="Email" required /></div>
            <div class="col-md-2"><input name="Phone" class="form-control form-control-sm" placeholder="Phone" /></div>
            <div class="col-md-1"><button type="submit" class="btn btn-primary btn-sm w-100">Add</button></div>
        </div>
    </form>
</div>

<partial name="_Board" model="Model" />

@section Scripts {
    <script src="~/lib/sortablejs/Sortable.min.js"></script>
    <script src="~/lib/htmx/dist/htmx.min.js"></script>
    <script>
        function initBoard() {
            document.querySelectorAll('.board-cards').forEach(function (el) {
                Sortable.create(el, {
                    group: 'stages',
                    animation: 120,
                    onEnd: function (evt) {
                        const card = evt.item;                       // the moved .board-card form
                        const destCol = evt.to.closest('.board-col');
                        const stageId = destCol.getAttribute('data-stage-id');
                        card.querySelector('.to-stage').value = stageId;
                        htmx.trigger(card, 'submit');                // posts the move, swaps #board-container
                    }
                });
            });
            // fallback: the per-card select submits the same form
            document.querySelectorAll('.move-select').forEach(function (sel) {
                sel.addEventListener('change', function () {
                    const form = sel.closest('form');
                    form.querySelector('.to-stage').value = sel.value;
                    htmx.trigger(form, 'submit');
                });
            });
        }
        initBoard();
        document.body.addEventListener('htmx:afterSwap', function (e) {
            if (e.target.id === 'board-container') initBoard();
        });
    </script>
}
```

- [ ] **Step 6: Add a Board link from the Jobs list**

In `src/Ats.Web/Views/Jobs/Index.cshtml`, add a Board action link in the actions cell, before the Edit link:

```cshtml
                <a class="btn btn-sm btn-outline-primary" asp-controller="Board" asp-action="Index" asp-route-jobId="@j.Id">Board</a>
```

- [ ] **Step 7: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): per-job kanban board with drag-and-drop and fallback"
```

---

## Task 7: Application details / history view

**Files:**
- Create: `src/Ats.Web/Controllers/ApplicationsController.cs`.
- Create: `src/Ats.Web/Models/ApplicationDetailsViewModel.cs`.
- Create: `src/Ats.Web/Views/Applications/Details.cshtml`.

- [ ] **Step 1: Create `ApplicationDetailsViewModel.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Web.Models;

public class ApplicationDetailsViewModel
{
    public Application Application { get; set; } = default!;
    public string CandidateName { get; set; } = "";
    public List<PipelineStage> Stages { get; set; } = new();
    public List<ApplicationEvent> Events { get; set; } = new();

    public string StageName(int stageId) => Stages.FirstOrDefault(s => s.Id == stageId)?.Name ?? $"#{stageId}";
}
```

- [ ] **Step 2: Create `ApplicationsController.cs`**

```csharp
using Ats.Application.Applications;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly IApplicationService _service;
    public ApplicationsController(IApplicationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var app = await _service.GetAsync(id);
        if (app is null) return NotFound();
        var stages = await _service.GetStagesForJobAsync(app.JobId);
        var events = await _service.ListEventsAsync(id);
        var name = (await _service.ListForJobAsync(app.JobId))
            .FirstOrDefault(a => a.Id == id)?.Candidate?.FullName ?? "(unknown)";
        return View(new ApplicationDetailsViewModel
        {
            Application = app, CandidateName = name, Stages = stages, Events = events
        });
    }
}
```

- [ ] **Step 3: Create `Views/Applications/Details.cshtml`**

```cshtml
@model Ats.Web.Models.ApplicationDetailsViewModel
@{ ViewData["Title"] = $"Application: {Model.CandidateName}"; }
<div class="mb-3">
    <a class="btn btn-outline-secondary btn-sm" asp-controller="Board" asp-action="Index" asp-route-jobId="@Model.Application.JobId">
        <i class="bi bi-arrow-left"></i> Board
    </a>
</div>
<dl class="row">
    <dt class="col-sm-3">Candidate</dt><dd class="col-sm-9">@Model.CandidateName</dd>
    <dt class="col-sm-3">Current stage</dt><dd class="col-sm-9">@Model.StageName(Model.Application.CurrentStageId)</dd>
    <dt class="col-sm-3">Status</dt><dd class="col-sm-9">@Model.Application.Status</dd>
    <dt class="col-sm-3">Applied</dt><dd class="col-sm-9">@Model.Application.AppliedAt.ToLocalTime().ToString("g")</dd>
</dl>

<h2 class="h5 mt-4">Stage history</h2>
<ul class="list-group">
    @foreach (var e in Model.Events)
    {
        <li class="list-group-item d-flex justify-content-between">
            <span>
                @(e.FromStageId is null ? "Applied" : Model.StageName(e.FromStageId.Value))
                <i class="bi bi-arrow-right mx-1"></i>
                @Model.StageName(e.ToStageId)
            </span>
            <span class="text-muted small">@e.OccurredAt.ToLocalTime().ToString("g")</span>
        </li>
    }
    @if (Model.Events.Count == 0) { <li class="list-group-item text-muted">No history yet.</li> }
</ul>
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(web): application details and stage history view"
```

---

## Task 8: Manual end-to-end verification of Plan B (and Phase 1)

**No new files.** Run after the migration is applied.

- [ ] **Step 1: Run the app and sign in** (`dotnet run --project src/Ats.Web`, https).

- [ ] **Step 2: Candidates** - create a candidate from the Candidates screen; confirm it lists; confirm a duplicate email is rejected.

- [ ] **Step 3: Add to job** - open a published job's Board; use Add candidate to add someone by email; confirm a card appears in the first stage. Add the same email again to the same job; confirm no duplicate application is created.

- [ ] **Step 4: Move via drag-and-drop** - drag a card to another column; confirm it stays after the board refreshes and that the column counts update.

- [ ] **Step 5: Move via fallback** - use a card's stage dropdown to move it; confirm the same result. Move a card to a terminal stage and confirm the application status becomes Hired or Rejected (check the Details view).

- [ ] **Step 6: History** - open a card's Details; confirm an ordered list of moves (starting with Applied) with timestamps.

- [ ] **Step 7: Concurrency** - open the same board in two tabs; move a card in tab 1, then move the same card in tab 2 using its stale row version; confirm the friendly "changed by someone else, reload" message appears (no crash).

- [ ] **Step 8: Tenancy** - sign in as a second tenant and confirm none of tenant 1's candidates, applications, or boards are visible.

- [ ] **Step 9: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 1 Plan B complete and verified"
```

---

## Task 9: Phase 1 knowledge-base update (spec Section 16 gate)

**Files:**
- Create: `.claude/skills/entities/SKILL.md`, `.claude/skills/pipeline/SKILL.md`.
- Modify: `CLAUDE.md` (skill-index rows).

- [ ] **Step 1: Create `.claude/skills/entities/SKILL.md`**

```markdown
---
name: entities
description: The Ats Phase 1 aggregates - Job, Candidate, Application, ApplicationEvent - their rules, soft delete, ExternalRef, and where the data-access lives. Read before touching recruiting data.
---

# Ats Entities (Phase 1)

## Aggregates
- `Job` (TenantEntity, ISoftDeletable): Draft/Published/Closed lifecycle; `ExternalRef` = `JOB-{n}` from
  `TenantSettings.LastJobNumber` (stable, never reused). References Department/Location/PipelineTemplate.
- `Candidate` (TenantEntity, ISoftDeletable): deduped per tenant by `Email` (unique `(TenantId, Email)`).
- `Application` (TenantEntity, ISoftDeletable): one per `(TenantId, JobId, CandidateId)`; `RowVersion`
  optimistic concurrency; `Status` Active/Hired/Rejected/Withdrawn; `CurrentStageId` points at a stage.
- `ApplicationEvent` (TenantEntity): append-only stage-move history (`FromStageId?`, `ToStageId`,
  `OccurredAt`, `MovedByUserId?`).

## Soft delete
`ISoftDeletable.IsDeleted` plus the global filter (`AtsDbContext`) hides deleted rows. Services set
`IsDeleted = true`; they never hard-delete Job/Candidate/Application. Departments/Locations are hard
delete, guarded against in-use.

## Data access
One service + repository interface per aggregate in `Ats.Application/<Area>`, implemented in
`Ats.Infrastructure/Persistence/Repositories`. Services return `OperationResult` (in
`Ats.Application/Departments/DepartmentService.cs`). Stage moves use
`IApplicationRepository.SetExpectedRowVersion` + `TrySaveChangesAsync` for concurrency.
```

- [ ] **Step 2: Create `.claude/skills/pipeline/SKILL.md`**

```markdown
---
name: pipeline
description: The Ats pipeline and candidate board - templates, stages, stage-to-status mapping, the kanban move flow, history, and concurrency. Read before changing pipeline or board behavior.
---

# Ats Pipelines and Board

## Templates and stages
`PipelineTemplate` has ordered `PipelineStage`s (`Order`, `IsTerminal`, `TerminalOutcome`,
`ReferralStatusOverride`). Edited via `IPipelineTemplateService.SaveAsync` which diffs the posted
stage set (add/update/remove by id, reorder by `Order`). A template used by a job cannot be deleted.

## Stage-to-status mapping
`ReferralStatusOverride` (defaults to the stage name) is what Phase 3 will send to ReferralTool as the
`CandidateStatus`. Phase 1 only stores it.

## Board and moves
`BoardController` renders columns (stages in order) with cards (active applications by `CurrentStageId`).
Moves post to `Board/Move` (drag-and-drop via SortableJS + htmx, or the per-card select fallback),
which calls `IApplicationService.MoveStageAsync`. A move appends an `ApplicationEvent`, sets terminal
status when the target stage is terminal, and uses `RowVersion` optimistic concurrency (a conflict
returns a friendly reload message). Every move (forward or backward) is recorded; nothing is emitted to
ReferralTool in Phase 1 (that is Phase 3).
```

- [ ] **Step 3: Add skill-index rows to `CLAUDE.md`**

After the UI row in the skill-index table, add:

```markdown
| Entities | `.claude/skills/entities/SKILL.md` | Job/Candidate/Application/Event, soft delete, ExternalRef |
| Pipeline | `.claude/skills/pipeline/SKILL.md` | Templates, stages, board moves, history, concurrency |
```

- [ ] **Step 4: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "docs: add entities and pipeline skills for Phase 1"
```

---

## Task 10: Existing-candidate picker (post-plan enhancement)

Added after Plan B execution to close a UX gap: the board's Add-candidate form forced retyping and
there was no way to attach an already-created candidate to a job. Both add paths now share one service
helper.

**Files:**
- Modify: `src/Ats.Application/Applications/ApplicationService.cs` - add
  `AddExistingCandidateToJobAsync(jobId, candidateId)` and extract a private `CreateApplicationAsync`
  (validate job + first stage, dedupe, create application + initial event) used by both add paths.
- Modify: `src/Ats.Web/Models/AddCandidateViewModel.cs` - add `int? CandidateId`; make the
  new-candidate fields optional (validated in the controller only when no existing candidate is picked).
- Modify: `src/Ats.Web/Models/BoardViewModel.cs` - add `CandidateOptions` (`List<SelectListItem>`).
- Modify: `src/Ats.Web/Controllers/BoardController.cs` - inject `ICandidateService`; populate
  `CandidateOptions`; in `AddCandidate`, branch on `CandidateId` (attach existing) vs new fields.
- Modify: `src/Ats.Web/Views/Board/Index.cshtml` - candidate picker select plus JS that hides the
  new-candidate fields (and drops their `required`) when an existing candidate is chosen.
- Create: `src/Ats.Web/Models/CandidatesIndexViewModel.cs` - candidates + published-job options.
- Modify: `src/Ats.Web/Controllers/CandidatesController.cs` - inject `IJobService` + `IApplicationService`;
  `Index` returns the view model; add `AddToJob(candidateId, jobId)` that calls
  `AddExistingCandidateToJobAsync` and redirects to the board.
- Modify: `src/Ats.Web/Views/Candidates/Index.cshtml` - per-row "Add to job" published-job dropdown.

Behavior: picking an existing candidate attaches with no retyping (new-candidate fields are ignored);
choosing "New candidate" create-or-matches by email as before. The dedupe rule
(one application per candidate per job) is unchanged because both paths share `CreateApplicationAsync`.

Verification (build + run): on a published job's board, add an existing candidate via the picker (no
card duplicate if already on the job); add a brand-new candidate via the fields (appears on the
Candidates page too); from the Candidates list, add a candidate to a published job and land on its board.

---

## Self-review (completed by plan author)

- **Spec coverage (Plan B scope):** Candidate/Application/ApplicationEvent entities + ApplicationStatus + configs + DbSets (Task 1); migration created, developer applies (Task 2); candidate service + repo + DI (Task 3) and screens + sidebar (Task 4); application service with add-to-job (one-active-per-(candidate,job)) and concurrency-safe MoveStage writing ApplicationEvent + terminal status (Task 5); per-job board with SortableJS drag-and-drop + htmx + per-card fallback select + add-candidate (Task 6); details/history view (Task 7); verification incl. concurrency and cross-tenant (Task 8); Phase 1 knowledge-base skills (Task 9, spec Section 16 gate).
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion.
- **Type consistency:** `OperationResult` reused from `Ats.Application.Departments`. `AddCandidateToJobInput`, `BoardCard`/`BoardColumn`/`BoardViewModel`, repository members (`GetStagesForJobAsync`, `FindByCandidateJobAsync`, `SetExpectedRowVersion`, `TrySaveChangesAsync`) match between interface, implementation, service, and controller. `Application.Candidate` navigation is configured in `ApplicationConfiguration` and used by `ListForJobAsync`/board. `RowVersion` round-trips as base64 between `_Board.cshtml` and `BoardController.Move`.
- **Concurrency boundary:** `DbUpdateConcurrencyException` is caught only in the repository (`TrySaveChangesAsync`), keeping `Ats.Application` free of EF types.
- **Ordering:** Task 1 (entities) precedes the migration (Task 2) and the services/UI; every task builds green on its own (services in Tasks 3 and 5 compile before their controllers in Tasks 4 and 6).
```
