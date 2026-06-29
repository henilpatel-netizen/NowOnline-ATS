# ATS Phase 3 - Plan A: Vacancy Feed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose a CatsOne-compatible vacancy feed from `Ats.Api` that ReferralTool can pull with a per-tenant feed API key, returning the tenant's non-draft jobs with the correct status mapping.

**Architecture:** Wire `Ats.Api` to the shared infrastructure, add a feed API-key auth filter that resolves the tenant from a hashed `Authorization: Token` key into `HttpContext.Items` (the global filter then scopes the query), and a `FeedController` that maps jobs to the CatsOne JSON shape with pagination.

**Tech Stack:** .NET 10, ASP.NET Core Web API (controllers), EF Core 10, SHA-256 (System.Security.Cryptography).

**Reference spec:** `docs/specs/2026-06-29-ats-phase-3-integration-design.md` (Plan A of three). No migration: `TenantSettings.FeedApiKeyHash` already exists from Phase 0.

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`.
- **Commits are manual** (developer runs them). The AI pauses and asks.
- **No schema change** in Plan A. The feed key's hash is set manually for testing (a developer SQL step); the Generate/Regenerate UI is Plan C.
- **Working directory** is `D:\LiveProject\Ats`. Stop any running app before building (DLL lock).
- **No em dashes and no emoji** in generated files.
- Shared repositories are registered in `Ats.Infrastructure/DependencyInjection.cs`.

---

## File structure (created or modified)

```
src\Ats.Application\Integration\FeedApiKey.cs                          # NEW: generate/hash/verify
src\Ats.Application\Integration\IVacancyFeedRepository.cs              # NEW
src\Ats.Infrastructure\Persistence\Repositories\VacancyFeedRepository.cs # NEW
src\Ats.Infrastructure\DependencyInjection.cs                         # MODIFY: register feed repo
src\Ats.Api\Program.cs                                                # MODIFY: controllers + infra + connection string
src\Ats.Api\appsettings.json                                          # MODIFY: connection string
src\Ats.Api\Authentication\FeedApiKeyFilter.cs                        # NEW
src\Ats.Api\Models\Feed\FeedResponse.cs                               # NEW (CatsOne DTOs)
src\Ats.Api\Controllers\FeedController.cs                             # NEW
.claude\rules\multi-tenancy.md                                        # MODIFY: feed-key bypass note
```

---

## Task 1: Wire the Ats.Api host (controllers + infrastructure + connection string)

**Files:**
- Modify: `src/Ats.Api/Program.cs`, `src/Ats.Api/appsettings.json`.

- [ ] **Step 1: Replace `src/Ats.Api/Program.cs`** with the controller + infrastructure host (keeps Serilog and `/health`, drops the template WeatherForecast)

```csharp
using Ats.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddHealthChecks();
builder.Services.AddAtsInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddScoped<Ats.Api.Authentication.FeedApiKeyFilter>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
```

- [ ] **Step 2: Add the connection string to `src/Ats.Api/appsettings.json`**

Add a `ConnectionStrings` block matching the Web project (same database). The file currently has
`Logging` and `AllowedHosts`; add:

```json
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AtsDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Ats.Api`
Expected: FAILS, because `FeedApiKeyFilter` does not exist yet (created in Task 4). This is expected;
proceed. (If you prefer a green checkpoint, do Tasks 2-4 before building.)

- [ ] **Step 4: Commit** (developer; commit with Tasks 2-4 for a green checkpoint)

```bash
git add -A
git commit -m "chore(api): host wiring for the vacancy feed"
```

---

## Task 2: Feed API key helper

**Files:**
- Create: `src/Ats.Application/Integration/FeedApiKey.cs`.

- [ ] **Step 1: Create `FeedApiKey.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Ats.Application.Integration;

public static class FeedApiKey
{
    // The plaintext key shown to the user once (Plan C). URL-safe, high entropy.
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Only this hash is stored (TenantSettings.FeedApiKeyHash).
    public static string Hash(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(bytes);
    }

    // Constant-time comparison of a presented key against a stored hash.
    public static bool Verify(string presentedKey, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        var computed = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        byte[] stored;
        try { stored = Convert.FromBase64String(storedHash); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Ats.Application`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "feat: feed API key generate/hash/verify helper"
```

---

## Task 3: Vacancy feed repository

**Files:**
- Create: `src/Ats.Application/Integration/IVacancyFeedRepository.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Repositories/VacancyFeedRepository.cs`.
- Modify: `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `IVacancyFeedRepository.cs`**

```csharp
using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public interface IVacancyFeedRepository
{
    // Non-draft jobs for the current tenant (Published + Closed), paginated, with Location loaded.
    Task<(List<Job> Jobs, int Total)> GetPageAsync(int page, int perPage, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `VacancyFeedRepository.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class VacancyFeedRepository : IVacancyFeedRepository
{
    private readonly AtsDbContext _db;
    public VacancyFeedRepository(AtsDbContext db) => _db = db;

    public async Task<(List<Job> Jobs, int Total)> GetPageAsync(int page, int perPage, CancellationToken ct = default)
    {
        // Draft is never exposed; the global filter already excludes soft-deleted and scopes by tenant.
        var query = _db.Jobs.Include(j => j.Location)
            .Where(j => j.Status != JobStatus.Draft)
            .OrderBy(j => j.Id);

        var total = await query.CountAsync(ct);
        var jobs = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);
        return (jobs, total);
    }
}
```

- [ ] **Step 3: Register in `DependencyInjection.cs`**

Add `using Ats.Application.Integration;` at the top, and before `return services;`:

```csharp
        services.AddScoped<IVacancyFeedRepository, VacancyFeedRepository>();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Ats.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat: vacancy feed repository"
```

---

## Task 4: CatsOne response DTOs and FeedController

**Files:**
- Create: `src/Ats.Api/Models/Feed/FeedResponse.cs`.
- Create: `src/Ats.Api/Controllers/FeedController.cs`.

- [ ] **Step 1: Create `Models/Feed/FeedResponse.cs`** (the exact CatsOne shape; `JsonPropertyName` pins the keys regardless of naming policy)

```csharp
using System.Text.Json.Serialization;

namespace Ats.Api.Models.Feed;

public sealed class FeedResponse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("_embedded")] public FeedEmbedded Embedded { get; set; } = new();
}

public sealed class FeedEmbedded
{
    [JsonPropertyName("jobs")] public List<FeedJob> Jobs { get; set; } = new();
}

public sealed class FeedJob
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "H";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("location")] public FeedLocation Location { get; set; } = new();
    [JsonPropertyName("_embedded")] public FeedJobEmbedded Embedded { get; set; } = new();
}

public sealed class FeedLocation
{
    [JsonPropertyName("city")] public string? City { get; set; }
}

public sealed class FeedJobEmbedded
{
    [JsonPropertyName("status")] public FeedStatus Status { get; set; } = new();
}

public sealed class FeedStatus
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
```

- [ ] **Step 2: Create `Controllers/FeedController.cs`**

```csharp
using Ats.Api.Authentication;
using Ats.Api.Models.Feed;
using Ats.Application.Integration;
using Ats.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Api.Controllers;

[ApiController]
[Route("jobs")]
[ServiceFilter(typeof(FeedApiKeyFilter))]
public class FeedController : ControllerBase
{
    private readonly IVacancyFeedRepository _feed;
    public FeedController(IVacancyFeedRepository feed) => _feed = feed;

    [HttpPost("search")]
    public async Task<FeedResponse> Search([FromQuery] int per_page = 100, [FromQuery] int page = 1)
    {
        if (per_page <= 0) per_page = 100;
        if (page <= 0) page = 1;

        var (jobs, total) = await _feed.GetPageAsync(page, per_page);

        var response = new FeedResponse { Total = total, Count = jobs.Count };
        foreach (var j in jobs)
        {
            response.Embedded.Jobs.Add(new FeedJob
            {
                Id = j.ExternalRef,
                Type = "H",
                Title = j.Title,
                Location = new FeedLocation { City = j.Location?.City ?? j.Location?.Name },
                Embedded = new FeedJobEmbedded
                {
                    Status = new FeedStatus { Title = j.Status == JobStatus.Published ? "Actief" : "Gesloten" }
                }
            });
        }
        return response;
    }
}
```

- [ ] **Step 3: Build** (still fails until the filter exists in Task 5)

Run: `dotnet build src/Ats.Api`
Expected: FAILS on the missing `FeedApiKeyFilter` reference. Proceed to Task 5, then build green.

- [ ] **Step 4: Commit** (developer; with Task 5)

```bash
git add -A
git commit -m "feat(api): CatsOne feed response and FeedController"
```

---

## Task 5: Feed API-key auth filter + tenancy-rule note

**Files:**
- Create: `src/Ats.Api/Authentication/FeedApiKeyFilter.cs`.
- Modify: `.claude/rules/multi-tenancy.md`.

- [ ] **Step 1: Create `Authentication/FeedApiKeyFilter.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Ats.Api.Authentication;

// Resolves the tenant from an `Authorization: Token {feedKey}` header by matching the SHA-256 hash
// against TenantSettings.FeedApiKeyHash (an IgnoreQueryFilters lookup, since no tenant is resolved
// yet), then sets HttpContext.Items["TenantId"] so the global query filter scopes the feed query.
public sealed class FeedApiKeyFilter : IAsyncAuthorizationFilter
{
    private const string Scheme = "Token ";
    private readonly AtsDbContext _db;

    public FeedApiKeyFilter(AtsDbContext db) => _db = db;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Scheme, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var key = header[Scheme.Length..].Trim();
        if (key.Length == 0)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var hash = FeedApiKey.Hash(key);
        var settings = await _db.TenantSettings.IgnoreQueryFilters()
            .Where(s => s.FeedApiKeyHash == hash)
            .Select(s => new { s.TenantId })
            .FirstOrDefaultAsync();

        if (settings is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        context.HttpContext.Items["TenantId"] = settings.TenantId;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (The whole solution now compiles.)

- [ ] **Step 3: Add the feed-key bypass to `.claude/rules/multi-tenancy.md`**

After the career-site slug bullet, add:

```markdown
- The CatsOne vacancy feed (`Ats.Api`) resolves the tenant from a hashed `Authorization: Token` feed
  key: `FeedApiKeyFilter` matches `TenantSettings.FeedApiKeyHash` via `IgnoreQueryFilters` and sets
  `HttpContext.Items["TenantId"]`. This is a documented filter-bypass spot (no tenant claim on feed
  requests). Invalid/missing key returns 401.
```

- [ ] **Step 4: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(api): feed API-key auth filter and tenancy-rule note"
```

---

## Task 6: Manual verification of the feed

**No new files.** Requires a tenant with a Published job (from earlier phases).

- [ ] **Step 1: Choose a test feed key and compute its hash** (PowerShell, developer runs)

```powershell
$key = "test-feed-key-123"
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key)))
$hash
```
Note the printed hash.

- [ ] **Step 2: Set the hash on a tenant** (developer runs in SSMS/PMC; replace the slug and hash)

```sql
UPDATE s SET s.FeedApiKeyHash = '<hash-from-step-1>'
FROM TenantSettings s
JOIN Tenants t ON t.Id = s.TenantId
WHERE t.Slug = 'acme';
```

- [ ] **Step 3: Run the API**

Run: `dotnet run --project src/Ats.Api`
Note the listening URL (for example `https://localhost:7xxx`).

- [ ] **Step 4: Call the feed with the key** (developer; another terminal)

```bash
curl -sk -X POST "https://localhost:7xxx/jobs/search?per_page=100&page=1" \
  -H "Authorization: Token test-feed-key-123" \
  -H "Content-Type: application/json" \
  -d '{ "and": [ { "field": "is_published", "filter": "exactly", "value": true } ] }'
```
Expected: JSON with `count`, `total`, and `_embedded.jobs[]` where each job has `id` (the ExternalRef),
`type":"H"`, `title`, `location.city`, and `_embedded.status.title` = `"Actief"` for Published jobs (and
`"Gesloten"` for Closed). Draft jobs are absent.

- [ ] **Step 5: Confirm auth rejects bad keys**

```bash
curl -sk -o /dev/null -w "%{http_code}\n" -X POST "https://localhost:7xxx/jobs/search" -H "Authorization: Token wrong-key"
curl -sk -o /dev/null -w "%{http_code}\n" -X POST "https://localhost:7xxx/jobs/search"
```
Expected: `401` for both (wrong key and missing header). Stop the API.

- [ ] **Step 6: Tenancy check**

Set a feed key on a second tenant (repeat Steps 1-2 with a different key and slug) and confirm calling
with that key returns only that tenant's jobs, never the first tenant's.

- [ ] **Step 7: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 3 Plan A vacancy feed verified"
```

---

## Add-on (post-plan): Scalar API reference UI, dev-only

Added so the feed can be exercised from a browser. Outside the original Plan A spec; commit separately.

- `Scalar.AspNetCore` package added to `Ats.Api`.
- `Ats.Api/OpenApi/FeedSecuritySchemeTransformer.cs` adds an `apiKey` security scheme (`FeedToken`,
  header `Authorization`) to the OpenAPI document so Scalar shows an auth box; the value to enter is
  `Token {your-feed-key}`.
- `Program.cs`: `AddOpenApi(... AddDocumentTransformer<FeedSecuritySchemeTransformer>())`, and in
  Development only `MapOpenApi()` + `MapScalarApiReference()`. UI at `/scalar`, doc at
  `/openapi/v1.json`. Never enabled outside Development (security guidance).

Usage: run `Ats.Api`, browse `/scalar`, open `POST /jobs/search`, click Authorize, enter
`Token {your-feed-key}` (the key whose SHA-256 is in `TenantSettings.FeedApiKeyHash`), Send.

## Self-review (completed by plan author)

- **Spec coverage (Plan A):** `Ats.Api` host wiring (Task 1); feed key generate/hash/verify (Task 2);
  non-draft tenant-scoped feed query with pagination (Task 3); CatsOne response shape with `type":"H"`,
  `location.city`, Published->`Actief` / Closed->non-Actief, Draft excluded (Task 4); `Authorization:
  Token` API-key auth resolving the tenant via `IgnoreQueryFilters` into `Items["TenantId"]`, 401 on
  bad/missing key, plus the documented bypass note (Task 5); verification incl. auth rejection and
  cross-tenant (Task 6). `custom_fields` omitted per the spec.
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion. Tasks 1 and 4
  intentionally fail to build until Task 5 adds the filter; this is called out and they commit together.
- **Type consistency:** `FeedApiKey.Hash` used by both the filter (Task 5) and the verification (Task 6);
  `IVacancyFeedRepository.GetPageAsync` signature matches across interface (Task 3), impl (Task 3), and
  the controller call (Task 4); `FeedResponse`/`FeedJob`/`FeedLocation`/`FeedStatus` property and JSON
  names match the controller's construction. `FeedApiKeyFilter` namespace `Ats.Api.Authentication`
  matches the `Program.cs` registration and the controller's `[ServiceFilter]`.
- **No migration:** `FeedApiKeyHash` exists from Phase 0; Plan A adds no columns. Confirmed.
- **Ordering:** the solution builds green once Task 5 lands; the feed query depends only on Task 3, the
  controller on Tasks 3-5.
```
