# ATS Phase 3 - Plan B: Outbox, Worker, and ReferralTool Client

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a referred candidate first reaches a stage, durably enqueue a status update and have a background worker deliver it to ReferralTool in per-application order, with a vacancy pre-check, retries/backoff, dead-lettering, and a delivery log.

**Architecture:** An `IOutboxEnqueuer` writes an `OutboxMessage` (payload snapshot) in the same unit of work as the stage event. `Ats.Worker` polls the outbox across all tenants (filter-bypass), and for each message sets a settable `WorkerTenantContext` to that tenant, pre-checks the vacancy, posts the status update via a typed `IReferralToolClient` (dual `X-Api-Key` + `X-Auth-Token`), and records a `WebhookDelivery`.

**Tech Stack:** .NET 10, EF Core 10, `BackgroundService`, typed `HttpClient`, System.Text.Json.

**Reference spec:** `docs/specs/2026-06-29-ats-phase-3-integration-design.md` (Plan B of three).

---

## Conventions for this plan

- **Verification = build + run.** No test project. Each task ends with `dotnet build`.
- **Commits are manual** (developer). Migration created by the AI, applied by the developer.
- **Working directory** `D:\LiveProject\Ats`. Stop any running app before building (DLL lock).
- **No em dashes and no emoji** in generated files.
- New shared services register in `Ats.Infrastructure/DependencyInjection.cs`; worker-only services register in `Ats.Worker/Program.cs`.

---

## File structure (created or modified)

```
src\Ats.Domain\Enums\OutboxStatus.cs, DeliveryKind.cs                  # NEW
src\Ats.Domain\Entities\OutboxMessage.cs, WebhookDelivery.cs           # NEW
src\Ats.Domain\Entities\TenantSettings.cs                             # MODIFY: ReferralToolApiKey
src\Ats.Infrastructure\Persistence\Configurations\OutboxMessageConfiguration.cs, WebhookDeliveryConfiguration.cs # NEW
src\Ats.Infrastructure\Persistence\AtsDbContext.cs                    # MODIFY: DbSets
src\Ats.Infrastructure\Migrations\*_AddOutboxAndWebhookDelivery.cs    # NEW (generated)
src\Ats.Application\Integration\IOutboxEnqueuer.cs                    # NEW
src\Ats.Infrastructure\Integration\OutboxEnqueuer.cs                  # NEW
src\Ats.Application\Applications\ApplicationService.cs                # MODIFY: enqueue
src\Ats.Application\Career\CareerService.cs                           # MODIFY: enqueue
src\Ats.Infrastructure\DependencyInjection.cs                        # MODIFY: register enqueuer
src\Ats.Application\Integration\ReferralToolContracts.cs, IReferralToolClient.cs, IntegrationOptions.cs, OutboxProcessing.cs # NEW
src\Ats.Infrastructure\Integration\ReferralToolClient.cs             # NEW
src\Ats.Infrastructure\Tenancy\WorkerTenantContext.cs                # NEW
src\Ats.Infrastructure\Integration\OutboxClaimStore.cs, OutboxProcessor.cs # NEW
src\Ats.Worker\OutboxDrainer.cs, Program.cs, appsettings.json        # NEW/MODIFY
.claude\rules\multi-tenancy.md                                       # MODIFY: worker bypass note
```

---

## Task 1: Entities, enums, TenantSettings column, configs, DbSets

**Files:**
- Create: `src/Ats.Domain/Enums/OutboxStatus.cs`, `DeliveryKind.cs`, `src/Ats.Domain/Entities/OutboxMessage.cs`, `WebhookDelivery.cs`.
- Modify: `src/Ats.Domain/Entities/TenantSettings.cs`.
- Create: `src/Ats.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`, `WebhookDeliveryConfiguration.cs`.
- Modify: `src/Ats.Infrastructure/Persistence/AtsDbContext.cs`.

- [ ] **Step 1: Create `OutboxStatus.cs`**

```csharp
namespace Ats.Domain.Enums;

public enum OutboxStatus
{
    Pending = 0,
    Delivered = 1,
    Failed = 2
}
```

- [ ] **Step 2: Create `DeliveryKind.cs`**

```csharp
namespace Ats.Domain.Enums;

public enum DeliveryKind
{
    CheckVacancy = 0,
    StatusUpdate = 1
}
```

- [ ] **Step 3: Create `OutboxMessage.cs`**

```csharp
using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class OutboxMessage : TenantEntity
{
    public int ApplicationId { get; set; }
    // Payload snapshot (what we will POST to ReferralTool).
    public string Code { get; set; } = string.Empty;
    public string ExternalVacancyId { get; set; } = string.Empty;
    public string ExternalCandidateId { get; set; } = string.Empty;
    public string? CandidateStatus { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
```

- [ ] **Step 4: Create `WebhookDelivery.cs`**

```csharp
using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class WebhookDelivery : TenantEntity
{
    public int OutboxMessageId { get; set; }
    public DeliveryKind Kind { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public int? HttpStatus { get; set; }
    public string? ResponseBody { get; set; }
    public bool Success { get; set; }
}
```

- [ ] **Step 5: Add `ReferralToolApiKey` to `TenantSettings.cs`**

Add to the class:

```csharp
    public string? ReferralToolApiKey { get; set; }
```

- [ ] **Step 6: Create `OutboxMessageConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.HasKey(m => m.Id);
        b.Property(m => m.Code).IsRequired().HasMaxLength(36);
        b.Property(m => m.ExternalVacancyId).IsRequired().HasMaxLength(36);
        b.Property(m => m.ExternalCandidateId).IsRequired().HasMaxLength(36);
        b.Property(m => m.CandidateStatus).HasMaxLength(200);
        b.Property(m => m.LastError).HasMaxLength(1000);
        b.Property(m => m.RowVersion).IsRowVersion();
        b.HasIndex(m => new { m.TenantId, m.Status, m.NextAttemptAt });
        b.HasIndex(m => new { m.TenantId, m.ApplicationId, m.Id });
    }
}
```

- [ ] **Step 7: Create `WebhookDeliveryConfiguration.cs`**

```csharp
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.ResponseBody).HasMaxLength(2000);
        b.HasIndex(d => new { d.TenantId, d.OutboxMessageId });
        b.HasOne<OutboxMessage>().WithMany().HasForeignKey(d => d.OutboxMessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 8: Add DbSets in `AtsDbContext`**

```csharp
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
```

- [ ] **Step 9: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit** (developer)

```bash
git add -A
git commit -m "feat(domain): outbox message and webhook delivery entities"
```

---

## Task 2: Migration

**Files:**
- Create: `src/Ats.Infrastructure/Migrations/*_AddOutboxAndWebhookDelivery.cs`.

- [ ] **Step 1: Create the migration**

```bash
cd /d/LiveProject/Ats
dotnet ef migrations add AddOutboxAndWebhookDelivery --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```
Expected: a migration creating `OutboxMessages` (with a `rowversion`) and `WebhookDeliveries`, and adding
`ReferralToolApiKey` to `TenantSettings`.

- [ ] **Step 2: Sanity-check**

```bash
grep -hoE "name: \"(OutboxMessages|WebhookDeliveries)\"|ReferralToolApiKey|RowVersion" src/Ats.Infrastructure/Migrations/*_AddOutboxAndWebhookDelivery.cs | sort -u
```
Expected: both tables, the column, and RowVersion.

- [ ] **Step 3: Build**, then developer applies:

```bash
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

- [ ] **Step 4: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): migration for outbox and webhook delivery"
```

---

## Task 3: Outbox enqueuer and wiring

**Files:**
- Create: `src/Ats.Application/Integration/IOutboxEnqueuer.cs`, `src/Ats.Infrastructure/Integration/OutboxEnqueuer.cs`.
- Modify: `src/Ats.Application/Applications/ApplicationService.cs`, `src/Ats.Application/Career/CareerService.cs`, `src/Ats.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 1: Create `IOutboxEnqueuer.cs`**

```csharp
namespace Ats.Application.Integration;

public interface IOutboxEnqueuer
{
    // Stages an OutboxMessage in the current unit of work (no SaveChanges) when this is the first
    // time the application reaches the stage, the application carries a SourceCode, and the tenant's
    // integration is enabled. The caller saves.
    Task StageAsync(int applicationId, int toStageId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `OutboxEnqueuer.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxEnqueuer : IOutboxEnqueuer
{
    private readonly AtsDbContext _db;
    public OutboxEnqueuer(AtsDbContext db) => _db = db;

    public async Task StageAsync(int applicationId, int toStageId, CancellationToken ct = default)
    {
        // First arrival only: count SAVED events to this stage (the just-added event is not yet saved).
        var alreadyReached = await _db.ApplicationEvents
            .CountAsync(e => e.ApplicationId == applicationId && e.ToStageId == toStageId, ct);
        if (alreadyReached > 0) return;

        var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null || string.IsNullOrWhiteSpace(app.SourceCode)) return;

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (settings is null || !settings.IntegrationEnabled || settings.ReferralToolCustomerId is null) return;

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == app.JobId, ct);
        var candidate = await _db.Candidates.FirstOrDefaultAsync(c => c.Id == app.CandidateId, ct);
        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == toStageId, ct);
        if (job is null || candidate is null || stage is null) return;

        var status = string.IsNullOrWhiteSpace(stage.ReferralStatusOverride) ? stage.Name : stage.ReferralStatusOverride!;

        await _db.OutboxMessages.AddAsync(new OutboxMessage
        {
            ApplicationId = app.Id,
            Code = app.SourceCode!.Trim(),
            ExternalVacancyId = job.ExternalRef,
            ExternalCandidateId = candidate.Key.ToString("D"),
            CandidateStatus = status,
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTimeOffset.UtcNow
        }, ct);
        // No SaveChanges: the caller commits this with the stage event in one transaction.
    }
}
```

- [ ] **Step 3: Inject and call the enqueuer in `ApplicationService`**

In `ApplicationService`, add the field and constructor parameter:

```csharp
    private readonly IOutboxEnqueuer _outbox;
```
Update the constructor to accept `IOutboxEnqueuer outbox` and assign `_outbox = outbox;` (add
`using Ats.Application.Integration;`).

In `MoveStageAsync`, immediately before `_repo.SetExpectedRowVersion(application, rowVersion);` add:

```csharp
        await _outbox.StageAsync(application.Id, toStageId, ct);
```

In `CreateApplicationAsync`, replace the final event-save block:

```csharp
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
```
with:

```csharp
        await _repo.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = _currentUser.UserId
        }, ct);
        await _outbox.StageAsync(application.Id, firstStage.Id, ct);
        await _repo.SaveChangesAsync(ct);
        return OperationResult.Ok;
```

- [ ] **Step 4: Inject and call the enqueuer in `CareerService`**

Add `using Ats.Application.Integration;`, a field `private readonly IOutboxEnqueuer _outbox;`, the
constructor parameter `IOutboxEnqueuer outbox` with `_outbox = outbox;`. In `ApplyAsync`, replace the
final event-save block:

```csharp
        await _applications.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = null
        }, ct);
        await _applications.SaveChangesAsync(ct);
        return OperationResult.Ok;
```
with:

```csharp
        await _applications.AddEventAsync(new ApplicationEvent
        {
            ApplicationId = application.Id,
            FromStageId = null,
            ToStageId = firstStage.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            MovedByUserId = null
        }, ct);
        await _outbox.StageAsync(application.Id, firstStage.Id, ct);
        await _applications.SaveChangesAsync(ct);
        return OperationResult.Ok;
```

- [ ] **Step 5: Register the enqueuer in `DependencyInjection.cs`**

Add `using Ats.Infrastructure.Integration;` if not present, and before `return services;`:

```csharp
        services.AddScoped<IOutboxEnqueuer, OutboxEnqueuer>();
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat: enqueue outbox messages on first stage arrival"
```

---

## Task 4: ReferralTool client

**Files:**
- Create: `src/Ats.Application/Integration/ReferralToolContracts.cs`, `IReferralToolClient.cs`.
- Create: `src/Ats.Infrastructure/Integration/ReferralToolClient.cs`.

- [ ] **Step 1: Create `ReferralToolContracts.cs`**

```csharp
namespace Ats.Application.Integration;

public sealed record ReferralToolSettings(string BaseUrl, string ApiKey, string AuthToken, int CustomerId);

public sealed record StatusUpdateRequest(
    int CustomerId, string Code, string ExternalVacancyId, string ExternalCandidateId, string? CandidateStatus);

// Reached=false means a network/timeout error (transient). HttpStatus is 0 when not reached.
public sealed record ReferralCallResult(bool Reached, int HttpStatus, string? Body);
```

- [ ] **Step 2: Create `IReferralToolClient.cs`**

```csharp
namespace Ats.Application.Integration;

public interface IReferralToolClient
{
    Task<(ReferralCallResult Result, bool Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default);

    Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `ReferralToolClient.cs`**

```csharp
using System.Text;
using System.Text.Json;
using Ats.Application.Integration;

namespace Ats.Infrastructure.Integration;

public sealed class ReferralToolClient : IReferralToolClient
{
    private readonly HttpClient _http;
    public ReferralToolClient(HttpClient http) => _http = http;

    public async Task<(ReferralCallResult Result, bool Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default)
    {
        using var request = Build(settings, "checkvacancyexists",
            new { CustomerId = settings.CustomerId, ExternalVacancyId = externalVacancyId });
        try
        {
            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var exists = false;
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("exists", out var e) && e.ValueKind == JsonValueKind.True)
                        exists = true;
                }
                catch (JsonException) { }
            }
            return (new ReferralCallResult(true, (int)resp.StatusCode, body), exists);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (new ReferralCallResult(false, 0, ex.Message), false);
        }
    }

    public async Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest r, CancellationToken ct = default)
    {
        using var request = Build(settings, "candidatestatusupdate",
            new { r.CustomerId, r.Code, r.ExternalVacancyId, r.ExternalCandidateId, r.CandidateStatus });
        try
        {
            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new ReferralCallResult(true, (int)resp.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ReferralCallResult(false, 0, ex.Message);
        }
    }

    private static HttpRequestMessage Build(ReferralToolSettings s, string action, object payload)
    {
        var url = $"{s.BaseUrl.TrimEnd('/')}/v1.0/kafka/{action}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("X-Api-Key", s.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Auth-Token", s.AuthToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Ats.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): ReferralTool HTTP client"
```

---

## Task 5: Worker tenant context, options, claim store, processor

**Files:**
- Create: `src/Ats.Application/Integration/IntegrationOptions.cs`, `OutboxProcessing.cs`.
- Create: `src/Ats.Infrastructure/Tenancy/WorkerTenantContext.cs`.
- Create: `src/Ats.Infrastructure/Integration/OutboxClaimStore.cs`, `OutboxProcessor.cs`.

- [ ] **Step 1: Create `IntegrationOptions.cs`**

```csharp
namespace Ats.Application.Integration;

public sealed class IntegrationOptions
{
    public int PollSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
    public int MaxAttempts { get; set; } = 48;
    public int BaseBackoffSeconds { get; set; } = 30;
    public int MaxBackoffSeconds { get; set; } = 1800;
}
```

- [ ] **Step 2: Create `OutboxProcessing.cs`** (claim record, outcome, and the two worker abstractions)

```csharp
namespace Ats.Application.Integration;

public sealed record OutboxClaim(int Id, int TenantId, int ApplicationId);

public enum OutboxOutcome { Delivered, Transient, Failed, Skip }

public interface IOutboxClaimStore
{
    // Due Pending messages across ALL tenants, oldest-first per application (filter-bypass read).
    Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default);
}

public interface IOutboxProcessor
{
    // Processes one message: sets the worker tenant, pre-checks the vacancy, posts the status update,
    // updates the message, and logs a WebhookDelivery.
    Task<OutboxOutcome> ProcessAsync(OutboxClaim claim, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `WorkerTenantContext.cs`**

```csharp
using Ats.Application.Abstractions;

namespace Ats.Infrastructure.Tenancy;

// Settable tenant context for the worker (no HttpContext). The drainer sets it per message so the
// global query filter, TenantId stamping, and WebhookDelivery insert all scope to that tenant.
public sealed class WorkerTenantContext : ITenantContext
{
    public int? CurrentTenantId { get; set; }
    public bool HasTenant => CurrentTenantId is not null;
}
```

- [ ] **Step 4: Create `OutboxClaimStore.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxClaimStore : IOutboxClaimStore
{
    private readonly AtsDbContext _db;
    public OutboxClaimStore(AtsDbContext db) => _db = db;

    public Task<List<OutboxClaim>> ClaimDueAsync(int max, DateTimeOffset now, CancellationToken ct = default) =>
        _db.OutboxMessages.IgnoreQueryFilters()
            .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.ApplicationId).ThenBy(m => m.Id)
            .Take(max)
            .Select(m => new OutboxClaim(m.Id, m.TenantId, m.ApplicationId))
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Create `OutboxProcessor.cs`**

```csharp
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ats.Infrastructure.Integration;

public sealed class OutboxProcessor : IOutboxProcessor
{
    private readonly AtsDbContext _db;
    private readonly WorkerTenantContext _tenant;
    private readonly IReferralToolClient _client;
    private readonly IntegrationOptions _opts;

    public OutboxProcessor(AtsDbContext db, WorkerTenantContext tenant, IReferralToolClient client, IOptions<IntegrationOptions> opts)
    {
        _db = db; _tenant = tenant; _client = client; _opts = opts.Value;
    }

    public async Task<OutboxOutcome> ProcessAsync(OutboxClaim claim, CancellationToken ct = default)
    {
        _tenant.CurrentTenantId = claim.TenantId; // scope everything below to this tenant

        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == claim.Id, ct);
        if (msg is null || msg.Status != OutboxStatus.Pending) return OutboxOutcome.Skip;

        var s = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (s is null || !s.IntegrationEnabled || s.ReferralToolCustomerId is null
            || string.IsNullOrWhiteSpace(s.ReferralToolBaseUrl)
            || string.IsNullOrWhiteSpace(s.ReferralToolApiKey)
            || string.IsNullOrWhiteSpace(s.ReferralToolAuthToken))
        {
            return await DeferAsync(msg, "Integration settings incomplete or disabled.", ct);
        }

        var settings = new ReferralToolSettings(
            s.ReferralToolBaseUrl!, s.ReferralToolApiKey!, s.ReferralToolAuthToken!, s.ReferralToolCustomerId.Value);

        // Pre-flight: only send once ReferralTool has imported the vacancy.
        var (check, exists) = await _client.CheckVacancyExistsAsync(settings, msg.ExternalVacancyId, ct);
        await LogAsync(msg.Id, DeliveryKind.CheckVacancy, check, exists, ct);
        if (!check.Reached || check.HttpStatus is < 200 or >= 300)
            return await DeferAsync(msg, $"Vacancy check failed ({check.HttpStatus}).", ct);
        if (!exists)
            return await DeferAsync(msg, "Vacancy not imported yet.", ct);

        var send = await _client.SendStatusUpdateAsync(settings,
            new StatusUpdateRequest(settings.CustomerId, msg.Code, msg.ExternalVacancyId, msg.ExternalCandidateId, msg.CandidateStatus), ct);
        await LogAsync(msg.Id, DeliveryKind.StatusUpdate, send, send.HttpStatus is >= 200 and < 300, ct);

        if (!send.Reached || send.HttpStatus >= 500)
            return await DeferAsync(msg, $"Transient send failure ({send.HttpStatus}).", ct);

        if (send.HttpStatus is >= 200 and < 300)
        {
            msg.Status = OutboxStatus.Delivered;
            msg.LastError = null;
            await _db.SaveChangesAsync(ct);
            return OutboxOutcome.Delivered;
        }

        // 4xx: terminal (duplicate event, unmapped status, bad code, validation).
        msg.Status = OutboxStatus.Failed;
        msg.LastError = Trunc($"{send.HttpStatus}: {send.Body}", 1000);
        await _db.SaveChangesAsync(ct);
        return OutboxOutcome.Failed;
    }

    private async Task<OutboxOutcome> DeferAsync(OutboxMessage msg, string error, CancellationToken ct)
    {
        msg.Attempts++;
        msg.LastError = Trunc(error, 1000);
        if (msg.Attempts >= _opts.MaxAttempts)
        {
            msg.Status = OutboxStatus.Failed;
            await _db.SaveChangesAsync(ct);
            return OutboxOutcome.Failed;
        }
        var seconds = Math.Min(_opts.MaxBackoffSeconds, _opts.BaseBackoffSeconds * Math.Pow(2, msg.Attempts));
        msg.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        await _db.SaveChangesAsync(ct);
        return OutboxOutcome.Transient;
    }

    private async Task LogAsync(int outboxMessageId, DeliveryKind kind, ReferralCallResult result, bool success, CancellationToken ct)
    {
        await _db.WebhookDeliveries.AddAsync(new WebhookDelivery
        {
            OutboxMessageId = outboxMessageId,
            Kind = kind,
            AttemptedAt = DateTimeOffset.UtcNow,
            HttpStatus = result.Reached ? result.HttpStatus : null,
            ResponseBody = Trunc(result.Body, 2000),
            Success = success
        }, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static string? Trunc(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Ats.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit** (developer)

```bash
git add -A
git commit -m "feat(infra): worker tenant context, outbox claim store and processor"
```

---

## Task 6: Worker host (drainer + wiring)

**Files:**
- Create: `src/Ats.Worker/OutboxDrainer.cs`.
- Modify: `src/Ats.Worker/Program.cs`, `src/Ats.Worker/appsettings.json`.
- Delete: `src/Ats.Worker/Worker.cs` (the template's sample hosted service), if present.

- [ ] **Step 1: Create `OutboxDrainer.cs`**

```csharp
using Ats.Application.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ats.Worker;

public sealed class OutboxDrainer : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IntegrationOptions _opts;
    private readonly ILogger<OutboxDrainer> _logger;

    public OutboxDrainer(IServiceProvider services, IOptions<IntegrationOptions> opts, ILogger<OutboxDrainer> logger)
    {
        _services = services; _opts = opts.Value; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<OutboxClaim> claims;
                using (var scope = _services.CreateScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IOutboxClaimStore>();
                    claims = await store.ClaimDueAsync(_opts.BatchSize, DateTimeOffset.UtcNow, stoppingToken);
                }

                // Per-application ordering: process each application's messages oldest-first and stop
                // the chain on the first non-delivered outcome so message N+1 never precedes N.
                foreach (var group in claims.GroupBy(c => (c.TenantId, c.ApplicationId)))
                {
                    foreach (var claim in group.OrderBy(c => c.Id))
                    {
                        using var ms = _services.CreateScope();
                        var processor = ms.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                        var outcome = await processor.ProcessAsync(claim, stoppingToken);
                        if (outcome != OutboxOutcome.Delivered) break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox drain cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opts.PollSeconds), stoppingToken);
        }
    }
}
```

- [ ] **Step 2: Replace `src/Ats.Worker/Program.cs`**

```csharp
using Ats.Application.Abstractions;
using Ats.Application.Integration;
using Ats.Infrastructure;
using Ats.Infrastructure.Integration;
using Ats.Infrastructure.Tenancy;
using Ats.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAtsInfrastructure(builder.Configuration);

// The worker has no HttpContext: replace the HTTP tenant context with a settable one.
builder.Services.RemoveAll<ITenantContext>();
builder.Services.AddScoped<WorkerTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());

builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection("Integration"));
builder.Services.AddHttpClient<IReferralToolClient, ReferralToolClient>();
builder.Services.AddScoped<IOutboxClaimStore, OutboxClaimStore>();
builder.Services.AddScoped<IOutboxProcessor, OutboxProcessor>();
builder.Services.AddHostedService<OutboxDrainer>();

var host = builder.Build();
host.Run();
```

- [ ] **Step 3: Delete the template `Worker.cs`** (its sample hosted service is replaced by `OutboxDrainer`)

```bash
rm -f src/Ats.Worker/Worker.cs
```

- [ ] **Step 4: Set `src/Ats.Worker/appsettings.json`** (connection string + integration options)

Ensure the file contains a connection string and an Integration section:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AtsDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Integration": {
    "PollSeconds": 15,
    "BatchSize": 100,
    "MaxAttempts": 48,
    "BaseBackoffSeconds": 30,
    "MaxBackoffSeconds": 1800
  }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit** (developer)

```bash
git add -A
git commit -m "feat(worker): outbox drainer host with per-application ordering and retries"
```

---

## Task 7: Multi-tenancy rule note

**Files:**
- Modify: `.claude/rules/multi-tenancy.md`.

- [ ] **Step 1: Add the worker bypass note**

After the feed-key bypass bullet, add:

```markdown
- The outbox worker (`Ats.Worker`) drains `OutboxMessages` across all tenants with `IgnoreQueryFilters`
  (`OutboxClaimStore`), then sets a settable `WorkerTenantContext.CurrentTenantId` to each message's
  `TenantId` before processing, so per-tenant reads, `TenantId` stamping, and the `WebhookDelivery`
  insert scope correctly. The worker registers `WorkerTenantContext` in place of `HttpTenantContext`
  (no HttpContext). This is a documented filter-bypass spot.
```

- [ ] **Step 2: Build** (docs only)

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (developer)

```bash
git add -A
git commit -m "docs: document the worker cross-tenant outbox bypass"
```

---

## Task 8: Manual end-to-end verification

**No new files.** Needs the migration applied and (for a full loop) a configured ReferralTool test
customer. Without ReferralTool, you can still verify enqueue + worker behavior via the DB.

- [ ] **Step 1: Enable integration for a tenant** (developer SQL; replace slug). For a DB-only test you
  can point at a stub; the vacancy pre-check will keep messages Pending (transient) until a real
  ReferralTool answers, which is correct behavior.

```sql
UPDATE s SET s.IntegrationEnabled = 1, s.ReferralToolCustomerId = 42,
             s.ReferralToolBaseUrl = 'https://referraltool.test',
             s.ReferralToolApiKey = 'rt-api-key', s.ReferralToolAuthToken = 'kafka-auth-token'
FROM TenantSettings s JOIN Tenants t ON t.Id = s.TenantId WHERE t.Slug = 'acme';
```

- [ ] **Step 2: Produce an outbox row.** Run `Ats.Web`, apply on the career site with a `?ref=` code to a
  published job (or move a referred candidate forward on the board). Confirm in SQL a row appears:

```sql
SELECT Id, ApplicationId, Code, ExternalVacancyId, ExternalCandidateId, CandidateStatus, Status, Attempts FROM OutboxMessages;
```
Expected: one Pending row with the captured `Code`, the job's `ExternalRef`, the candidate `Key`, and the
mapped stage status. Applying without a code, or moving backward to an already-reached stage, produces
no row.

- [ ] **Step 3: Run the worker.** `dotnet run --project src/Ats.Worker`. Watch the logs and the tables:

```sql
SELECT * FROM WebhookDeliveries ORDER BY Id DESC;
SELECT Id, Status, Attempts, NextAttemptAt, LastError FROM OutboxMessages;
```
Against a configured ReferralTool: the message becomes `Delivered` and a `StatusUpdate` delivery row
shows the 2xx. Against an unreachable/test endpoint: a `CheckVacancy` delivery is logged and the message
stays `Pending` with an incremented `Attempts` and a future `NextAttemptAt` (transient retry), which is
the intended "vacancy not imported yet" behavior.

- [ ] **Step 4: Ordering and dedupe.** Move the same candidate through several stages; confirm one
  OutboxMessage per first-arrival stage, none for backward moves, and that for one application messages
  are delivered in `Id` order.

- [ ] **Step 5: Tenancy.** Confirm a second tenant's messages are processed under its own
  `TenantId`/settings and that `WebhookDelivery` rows carry the right `TenantId`.

- [ ] **Step 6: Final commit** (developer)

```bash
git add -A
git commit -m "chore: Phase 3 Plan B outbox/worker verified"
```

---

## Self-review (completed by plan author)

- **Spec coverage (Plan B):** `OutboxMessage` + `WebhookDelivery` + `ReferralToolApiKey` (Task 1);
  migration (Task 2); first-arrival, code-present, integration-enabled enqueue wired into both the
  back-office move/add and the public apply, atomically with the stage event (Task 3); ReferralTool
  client with dual headers and the `/v1.0/kafka/...` routes (Task 4); worker tenant context, options,
  cross-tenant claim, and the processor with vacancy pre-check + 2xx/5xx/4xx classification + backoff +
  dead-letter + delivery logging (Task 5); the drainer host with per-application ordering (Task 6); the
  documented worker bypass (Task 7); verification incl. enqueue rules, ordering, transient retry, and
  tenancy (Task 8).
- **Placeholder scan:** no TBD/TODO; every code step shows full file or exact insertion/replacement.
- **Type consistency:** `IOutboxEnqueuer.StageAsync(int,int,CancellationToken)` matches caller sites;
  `ReferralToolSettings`/`StatusUpdateRequest`/`ReferralCallResult` are shared by client interface, impl,
  and processor; `OutboxClaim`/`OutboxOutcome`/`IOutboxClaimStore`/`IOutboxProcessor` match across the
  store, processor, and drainer; `IntegrationOptions` bound from the `Integration` config section and
  injected via `IOptions<>`; `WorkerTenantContext` implements `ITenantContext` and is swapped in only in
  the worker. `Candidate.Key.ToString("D")` yields the 36-char ExternalCandidateId.
- **Atomicity:** enqueue stages the outbox row in the same `DbContext` and is committed by the caller's
  existing save (the move's `TrySaveChangesAsync` or the apply's final `SaveChangesAsync`), so a stage
  event and its outbox row commit together. First-arrival is computed from saved events before the new
  event is persisted, so it is order-correct.
- **Concurrency/EF boundary:** `DbUpdateConcurrencyException` handling stays in Infrastructure (the move
  path, unchanged from Phase 1); the worker uses a single instance with `RowVersion` present as a guard.
- **Ordering:** every task builds green on its own; the worker host (Task 6) depends on the client (Task
  4) and the processor/store (Task 5).
```
