# Generic ReferralTool Vacancy Push Implementation Plan

> **For agentic workers:** Run through `/ats-ship docs/plans/2026-09-28-generic-referraltool-vacancy-push.md`
> (superpowers `subagent-driven-development` with the Ats overrides). Implementers never commit; they leave
> a working-tree diff. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the CatsOne-shaped pull feed (ReferralTool `ImportType` dependency) with a generic push of
vacancies from Ats to ReferralTool's REST vacancy API, identical for every tenant.

**Architecture:** Job lifecycle changes (publish, update, close, delete) stage a `VacancySync` outbox message
carrying a JSON snapshot of the vacancy, in the same unit of work as the job change. The existing worker
claims it (ordered per job), asks `checkvacancyexists`, then `POST /v1.0/vacancy` (new) or
`PUT /v1.0/vacancy/{id}` (existing, `Inactive` for closed/deleted). Candidate status delivery is unchanged.
The feed endpoint, feed key and the whole `Ats.Api` project are removed.

**Tech Stack:** .NET 10, EF Core (SQL Server), xUnit with hand-rolled fakes, System.Text.Json, HttpClient.

## Decisions (agreed with the developer on 2026-09-28)

- **No ReferralTool code change.** Only its existing endpoints are used.
- **Credentials stay per tenant** in the existing `TenantSettings` fields: `ReferralToolApiKey` (X-Api-Key,
  a ReferralTool `ApiKeys` row GUID) and `ReferralToolAuthToken` (X-Auth-Token, ReferralTool
  `Kafka:AuthToken`). Neither expires. The operator copies them from ReferralTool.
- **Feed removed** now, including `Ats.Api`. Unused `TenantSettings.FeedApiKeyHash` / `FeedLastPulledAt`
  columns stay one release (rollback safety) and are dropped in a follow-up migration.
- **Delete in Ats = `PUT Inactive=true`** in ReferralTool, never `DELETE`: ReferralTool's DELETE renames
  the ExternalId, which breaks later status updates and points for already-referred candidates. Matches
  the old importer, which deactivated vacancies missing from the feed.
- **Vacancy URL** = `{Integration:CareerSiteBaseUrl}/careers/{slug}/jobs/{ExternalRef}`, one host per
  environment.
- **Category** = Department name (single item, or none). **EmploymentType** = enum name (`FullTime` ...).
- **Title** truncated to 150 characters (ReferralTool max) in the payload.
- **Candidate side** keeps `POST /v1.0/kafka/candidatestatusupdate`.
- **Base URL** stays per tenant.

## ReferralTool behaviour this relies on (verified in ReferralTool source 2026-09-28)

| Call | Auth | Behaviour |
|---|---|---|
| `POST /v1.0/vacancy` | `X-Api-Key` (customer from the key) | Body `CreateVacancyModel`: `Id` req max 36, `Title` req max 150, `Url` req max 300, `Location` 150, `EmploymentType` 150, `MinHours`/`MaxHours` 0-168, `Education` 150, `Categories` list. 200 empty body. Existing Id -> 400 "Id is not unique". No `Inactive` field. |
| `PUT /v1.0/vacancy/{id}` | `X-Api-Key` | Body `UpdateVacancyModel`: same minus `Id`, plus `Inactive` (req). **Full replace**: omitted fields become null, `Categories` null clears them. Missing -> 400 "Vacancy does not exist". |
| `POST /v1.0/kafka/checkvacancyexists` | `X-Api-Key` + `X-Auth-Token` | `{ CustomerId, ExternalVacancyId }` -> `{ exists }`. The only clean existence check (`GET /vacancy/{id}` answers 400 for missing). |

Every vacancy-endpoint failure is HTTP 400 `ValidationProblemDetails`, including caught exceptions. A 400
whose `errors` object has entries is a model-validation failure (terminal); any other 400 is retried.

Operational prerequisite (manual, ReferralTool data, not code): for every customer served by Ats, set
`ImportSettings.Enabled = 0` so the Tasker importer no longer touches that customer's vacancies.

## File map

| File | Change |
|---|---|
| `docs/integration/referraltool-contract.md` | Appendix D (vacancy push); Appendix A marked removed |
| `src/Ats.Application/Integration/VacancyPayload.cs` | **new** snapshot record + pure mapper |
| `src/Ats.Application/Integration/ReferralToolRules.cs` | `DecideVacancyCall`, `ClassifyVacancyCall`, `HasValidationErrors` |
| `src/Ats.Application/Integration/ReferralToolContracts.cs` | `VacancyCall` enum |
| `src/Ats.Application/Integration/IntegrationOptions.cs` | `CareerSiteBaseUrl` |
| `src/Ats.Application/Integration/IOutboxEnqueuer.cs` | `StageVacancySyncAsync` |
| `src/Ats.Application/Integration/IReferralToolClient.cs` | `CreateVacancyAsync`, `UpdateVacancyAsync` |
| `src/Ats.Application/Integration/OutboxProcessing.cs` | `OutboxClaim.ApplicationId` nullable |
| `src/Ats.Application/Integration/IIntegrationSettingsService.cs` | `QueueVacancySyncAsync`, `CountPublishedJobsAsync`; remove `GenerateFeedKeyAsync` |
| `src/Ats.Application/Jobs/JobService.cs` | stage vacancy sync on publish/update/close/delete |
| `src/Ats.Domain/Enums/OutboxKind.cs` | **new** |
| `src/Ats.Domain/Enums/DeliveryKind.cs` | `VacancyCreate`, `VacancyUpdate` |
| `src/Ats.Domain/Entities/OutboxMessage.cs` | `Kind`, `JobId`, `Payload`, `ApplicationId` nullable |
| `src/Ats.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` | job index |
| `src/Ats.Infrastructure/Migrations/*_AddVacancySyncOutbox.cs` | **new** (generated) |
| `src/Ats.Infrastructure/Integration/OutboxClaimStore.cs` | per-job ordering |
| `src/Ats.Infrastructure/Integration/OutboxEnqueuer.cs` | vacancy snapshot staging |
| `src/Ats.Infrastructure/Integration/ReferralToolClient.cs` | vacancy calls |
| `src/Ats.Infrastructure/Integration/OutboxProcessor.cs` | vacancy branch |
| `src/Ats.Infrastructure/Integration/IntegrationSettingsService.cs` | sync-all, count, test sample; drop feed |
| `src/Ats.Web/Program.cs`, `src/Ats.Web/appsettings.Development.json` | `Integration:CareerSiteBaseUrl` |
| `src/Ats.Web/Controllers/IntegrationController.cs`, `Models/IntegrationSettingsViewModel.cs`, `Views/Integration/Index.cshtml`, `Views/Integration/_DeliveryRows.cshtml` | vacancy card, sync action, row labels |
| `src/Ats.Web/Views/Dashboard/Index.cshtml`, `Application/Dashboard/DashboardSummary.cs`, `Infrastructure/Dashboard/DashboardService.cs` | drop feed-pull line |
| `src/Ats.Api/**`, `Ats.slnx` | **deleted** |
| `FeedApiKey.cs`, `FeedPullThrottle.cs`, `IVacancyFeedRepository.cs`, `VacancyFeedRepository.cs`, `FeedPullThrottleTests.cs` | **deleted** |
| `tests/Ats.Tests/Integration/VacancyPayloadTests.cs`, `VacancyRulesTests.cs` | **new** |
| `tests/Ats.Tests/Jobs/JobServiceTests.cs`, `Fakes/FakeApplicationRepository.cs` | enqueue tests + fake |
| `tests/e2e/security.spec.ts` | remove the two feed tests |
| `.claude/skills/integration/SKILL.md`, `.claude/skills/multitenancy/SKILL.md`, `.claude/rules/multi-tenancy.md`, `.claude/skills/architecture/SKILL.md`, `CLAUDE.md` | docs |

---

### Task 1: Contract Appendix D

**Files:** Modify `docs/integration/referraltool-contract.md`

- [x] **Step 1:** Under the Appendix A heading insert:
  `> **Removed 2026-09-28.** Ats no longer serves a pull feed; vacancies are pushed (Appendix D).`
- [x] **Step 2:** Append a new section `# Appendix D -- VACANCY PUSH (Ats pushes, ReferralTool stores)` containing,
  verbatim, the "ReferralTool behaviour this relies on" table and the paragraph under it from this plan, plus:
  - Mapping: `Id`=`Job.ExternalRef`, `Title`=`Job.Title` (first 150 chars), `Url`=`{CareerSiteBaseUrl}/careers/{slug}/jobs/{ExternalRef}`,
    `Location`=`Location.City ?? Location.Name`, `EmploymentType`=enum name, `Categories`=`[Department.Name]` or `[]`,
    `Inactive`=`IsDeleted || Status != Published`. `MinHours`, `MaxHours`, `Education` not sent (null).
  - Algorithm per message: `checkvacancyexists`; exists -> PUT; not exists and active -> POST; not exists and
    inactive -> nothing. Ats never calls `DELETE` (it renames the ExternalId and orphans referred candidates).
  - Prerequisite: ReferralTool `ImportSettings.Enabled = 0` for customers served by Ats.
- [x] **Step 3:** Verify: `grep -n "Appendix D" docs/integration/referraltool-contract.md` shows the heading.

### Task 2: Vacancy payload and vacancy rules (pure, TDD)

**Files:**
- Create: `src/Ats.Application/Integration/VacancyPayload.cs`
- Modify: `src/Ats.Application/Integration/ReferralToolContracts.cs`, `src/Ats.Application/Integration/ReferralToolRules.cs`
- Test: `tests/Ats.Tests/Integration/VacancyPayloadTests.cs`, `tests/Ats.Tests/Integration/VacancyRulesTests.cs`

- [x] **Step 1: Write the failing tests**

`tests/Ats.Tests/Integration/VacancyPayloadTests.cs`:
```csharp
using Ats.Application.Integration;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Integration;

// The snapshot is what ReferralTool stores and shows referrers; its limits are ReferralTool's model limits.
public class VacancyPayloadTests
{
    private static Job PublishedJob() => new()
    {
        Id = 5, Title = "Senior .NET Developer", ExternalRef = "JOB-5",
        Status = JobStatus.Published, EmploymentType = EmploymentType.PartTime
    };

    [Fact]
    public void Maps_the_job_to_the_referraltool_shape()
    {
        var p = VacancyPayload.From(PublishedJob(), "https://careers.example.com/", "acme", "Amsterdam", "Engineering");

        Assert.Equal("JOB-5", p.Id);
        Assert.Equal("Senior .NET Developer", p.Title);
        Assert.Equal("https://careers.example.com/careers/acme/jobs/JOB-5", p.Url);
        Assert.Equal("Amsterdam", p.Location);
        Assert.Equal("PartTime", p.EmploymentType);
        Assert.Equal(new[] { "Engineering" }, p.Categories);
        Assert.False(p.Inactive);
    }

    [Fact]
    public void A_title_longer_than_referraltool_allows_is_cut_to_150()
    {
        var job = PublishedJob();
        job.Title = new string('x', 200);

        Assert.Equal(150, VacancyPayload.From(job, "https://c.example", "acme", null, null).Title.Length);
    }

    [Fact]
    public void No_department_means_no_categories() =>
        Assert.Empty(VacancyPayload.From(PublishedJob(), "https://c.example", "acme", null, null).Categories);

    [Theory]
    [InlineData(JobStatus.Closed, false)]
    [InlineData(JobStatus.Draft, false)]
    [InlineData(JobStatus.Published, true)]
    public void Only_a_live_published_job_is_active(JobStatus status, bool deleted)
    {
        var job = PublishedJob();
        job.Status = status;
        job.IsDeleted = deleted;

        Assert.True(VacancyPayload.From(job, "https://c.example", "acme", null, null).Inactive);
    }
}
```

`tests/Ats.Tests/Integration/VacancyRulesTests.cs`:
```csharp
using Ats.Application.Integration;
using Xunit;

namespace Ats.Tests.Integration;

// Vacancy sync sends current state, so a wrong decision either duplicates, loses or wrongly
// deactivates a vacancy referrers can see.
public class VacancyRulesTests
{
    [Theory]
    [InlineData(true, false, VacancyCall.Update)]
    [InlineData(true, true, VacancyCall.Update)]    // closed/deleted: PUT Inactive, never DELETE
    [InlineData(false, false, VacancyCall.Create)]
    [InlineData(false, true, VacancyCall.None)]     // nothing to deactivate
    public void Decides_the_call_from_existence_and_state(bool exists, bool inactive, VacancyCall expected) =>
        Assert.Equal(expected, ReferralToolRules.DecideVacancyCall(exists, inactive));

    private const string Validation =
        """{"title":"Creating vacancy failed due to validation errors","status":400,"errors":{"Title":["too long"]}}""";
    private const string NotUnique = """{"title":"Id is not unique","status":400,"errors":{}}""";

    [Theory]
    [InlineData(true, 200, null, OutboxOutcome.Delivered)]
    [InlineData(false, 0, null, OutboxOutcome.Transient)]
    [InlineData(true, 500, null, OutboxOutcome.Transient)]
    [InlineData(true, 401, null, OutboxOutcome.Postpone)]
    [InlineData(true, 403, null, OutboxOutcome.Postpone)]
    [InlineData(true, 400, Validation, OutboxOutcome.Failed)]
    [InlineData(true, 400, NotUnique, OutboxOutcome.Transient)]   // raced a create; next run PUTs
    [InlineData(true, 400, "not json", OutboxOutcome.Transient)]
    [InlineData(true, 404, null, OutboxOutcome.Transient)]
    public void Classifies_a_vacancy_call(bool reached, int status, string? body, OutboxOutcome expected) =>
        Assert.Equal(expected, ReferralToolRules.ClassifyVacancyCall(reached, status, body));
}
```

- [x] **Step 2:** Run `dotnet test Ats.slnx --filter "FullyQualifiedName~Vacancy"`. Expected: build FAIL (types missing).

- [x] **Step 3: Implement**

`src/Ats.Application/Integration/VacancyPayload.cs`:
```csharp
using Ats.Domain.Entities;
using Ats.Domain.Enums;

namespace Ats.Application.Integration;

// Snapshot of a job as ReferralTool's vacancy model sees it, taken when the job changes and sent as-is.
public sealed record VacancyPayload(
    string Id, string Title, string Url, string? Location, string EmploymentType,
    IReadOnlyList<string> Categories, bool Inactive)
{
    public const int MaxTitleLength = 150;

    public static VacancyPayload From(Job job, string careerSiteBaseUrl, string tenantSlug, string? location, string? department) =>
        new(job.ExternalRef,
            job.Title.Length <= MaxTitleLength ? job.Title : job.Title[..MaxTitleLength],
            $"{careerSiteBaseUrl.TrimEnd('/')}/careers/{Uri.EscapeDataString(tenantSlug)}/jobs/{Uri.EscapeDataString(job.ExternalRef)}",
            location,
            job.EmploymentType.ToString(),
            department is null ? [] : [department],
            job.IsDeleted || job.Status != JobStatus.Published);
}
```

Append to `src/Ats.Application/Integration/ReferralToolContracts.cs`:
```csharp
public enum VacancyCall { None, Create, Update }
```

Add to `ReferralToolRules` (after `ClassifyStatusUpdate`):
```csharp
    // Ats never DELETEs: ReferralTool renames a deleted vacancy's ExternalId, orphaning referred candidates.
    public static VacancyCall DecideVacancyCall(bool exists, bool inactive) =>
        exists ? VacancyCall.Update : inactive ? VacancyCall.None : VacancyCall.Create;

    // ReferralTool answers every vacancy failure with 400; only one carrying model errors is permanent.
    // Any other 400 (not unique, does not exist, a caught exception) reflects state that a retry re-reads.
    public static OutboxOutcome ClassifyVacancyCall(bool reached, int httpStatus, string? body)
    {
        if (!reached || httpStatus >= 500) return OutboxOutcome.Transient;
        if (httpStatus is >= 200 and < 300) return OutboxOutcome.Delivered;
        if (IsCredentialRejection(httpStatus)) return OutboxOutcome.Postpone;
        if (httpStatus == 400 && HasValidationErrors(body)) return OutboxOutcome.Failed;
        return OutboxOutcome.Transient;
    }

    public static bool HasValidationErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object
                && errors.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }
```

- [x] **Step 4:** Run `dotnet test Ats.slnx --filter "FullyQualifiedName~Vacancy"`. Expected: PASS. Mutation-check:
  flip `exists ? Update` to `exists ? None` and confirm a test fails, then restore.
- [x] **Step 5:** `dotnet build Ats.slnx` warning-clean, `dotnet format Ats.slnx --verify-no-changes` clean. Leave the diff.

### Task 3: Outbox schema for vacancy messages

**Files:**
- Create: `src/Ats.Domain/Enums/OutboxKind.cs`
- Modify: `src/Ats.Domain/Entities/OutboxMessage.cs`, `src/Ats.Domain/Enums/DeliveryKind.cs`,
  `src/Ats.Application/Integration/OutboxProcessing.cs`,
  `src/Ats.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`,
  `src/Ats.Infrastructure/Integration/OutboxClaimStore.cs`
- Create (generated): migration `AddVacancySyncOutbox`

- [x] **Step 1:** `src/Ats.Domain/Enums/OutboxKind.cs`:
```csharp
namespace Ats.Domain.Enums;

public enum OutboxKind
{
    CandidateStatus = 0,
    VacancySync = 1
}
```
- [x] **Step 2:** `DeliveryKind`: add `VacancyCreate = 2,` and `VacancyUpdate = 3` after `StatusUpdate = 1`.
- [x] **Step 3:** `OutboxMessage`: change `public int ApplicationId` to `public int? ApplicationId { get; set; }` and add
  directly below it:
```csharp
    public OutboxKind Kind { get; set; } = OutboxKind.CandidateStatus;
    public int? JobId { get; set; }
    // VacancySync only: the VacancyPayload JSON snapshot.
    public string? Payload { get; set; }
```
  Update the comment above `Code` to `// CandidateStatus payload snapshot (empty for VacancySync).`
- [x] **Step 4:** `OutboxProcessing.cs`: `OutboxClaim(int Id, int TenantId, int? ApplicationId, DateTimeOffset Lease)`.
  Update the `IOutboxClaimStore` comment to "never one with an older undelivered message of the same
  application or the same job".
- [x] **Step 5:** `OutboxMessageConfiguration`: after the `(TenantId, ApplicationId, Id)` index add
```csharp
        b.HasIndex(m => new { m.TenantId, m.JobId, m.Id }).HasFilter("[JobId] IS NOT NULL");
```
- [x] **Step 6:** `OutboxClaimStore.ClaimDueAsync`: replace the SQL with
```csharp
        const string sql = @"
WITH due AS (
    SELECT TOP({0}) Id, TenantId, ApplicationId, JobId, Status, NextAttemptAt
    FROM OutboxMessages AS m WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE m.Status IN ({1}, {2}) AND m.NextAttemptAt <= {3}
      AND NOT EXISTS (
          SELECT 1 FROM OutboxMessages AS older WITH (READCOMMITTEDLOCK)
          WHERE older.TenantId = m.TenantId AND older.ApplicationId = m.ApplicationId
            AND older.Id < m.Id AND older.Status IN ({1}, {2}))
      AND NOT EXISTS (
          SELECT 1 FROM OutboxMessages AS olderJob WITH (READCOMMITTEDLOCK)
          WHERE olderJob.TenantId = m.TenantId AND olderJob.JobId = m.JobId
            AND olderJob.Id < m.Id AND olderJob.Status IN ({1}, {2}))
    ORDER BY m.Id
)
UPDATE due SET Status = {2}, NextAttemptAt = {4}
OUTPUT inserted.Id, inserted.TenantId, inserted.ApplicationId, inserted.NextAttemptAt AS Lease;";
```
  and add one line to the method comment: "The same rule orders vacancy syncs per job (a NULL key never
  matches, so each kind only blocks its own stream). Two NOT EXISTS, not one with OR, so each uses its index."
- [x] **Step 7:** `dotnet build Ats.slnx` (fix any `int?` compile errors by comparison, never `.Value`).
- [x] **Step 8:** Generate the migration (allowed; applying it is not):
  `dotnet ef migrations add AddVacancySyncOutbox --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`.
  Check it only: adds `Kind` (int, default 0), `JobId` (int null), `Payload` (nvarchar(max) null), alters
  `ApplicationId` to nullable, creates the filtered index. Nothing else.
- [x] **Step 9:** `dotnet test Ats.slnx` green (`OutboxClaimTests` still compile), format clean. Leave the diff.
  Manual for the developer: `dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`.

### Task 4: Stage vacancy syncs from the job lifecycle (TDD)

**Files:**
- Modify: `src/Ats.Application/Integration/IOutboxEnqueuer.cs`, `src/Ats.Application/Integration/IntegrationOptions.cs`,
  `src/Ats.Application/Jobs/JobService.cs`, `src/Ats.Infrastructure/Integration/OutboxEnqueuer.cs`,
  `src/Ats.Web/Program.cs`, `src/Ats.Web/appsettings.Development.json`,
  `tests/Ats.Tests/Fakes/FakeApplicationRepository.cs`, `tests/Ats.Tests/Jobs/JobServiceTests.cs`

- [x] **Step 1:** `IOutboxEnqueuer` add:
```csharp
    // Stages a VacancySync snapshot of the job in the current unit of work (no SaveChanges) when the
    // tenant's integration is enabled. The caller saves, so the job change and its sync commit together.
    Task StageVacancySyncAsync(Job job, CancellationToken ct = default);
```
  (add `using Ats.Domain.Entities;`).
- [x] **Step 2:** `FakeOutboxEnqueuer` add:
```csharp
    public List<Job> VacancySyncs { get; } = new();

    public Task StageVacancySyncAsync(Job job, CancellationToken ct = default)
    {
        VacancySyncs.Add(job);
        return Task.CompletedTask;
    }
```
- [x] **Step 3: Write the failing tests** in `JobServiceTests`. Change `Build` to
```csharp
    private static (JobService Service, FakeJobRepository Repo, FakeOutboxEnqueuer Outbox) Build(params Job[] jobs)
    {
        var repo = new FakeJobRepository();
        repo.Jobs.AddRange(jobs);
        var outbox = new FakeOutboxEnqueuer();
        return (new JobService(repo, outbox), repo, outbox);
    }
```
  update existing deconstructions to `var (service, repo, _) = Build(...)`, and add:
```csharp
    // ---- ReferralTool vacancy sync -----------------------------------------------------------

    private static Job PublishedJob(int id = 1)
    {
        var job = DraftJob(id);
        job.Status = JobStatus.Published;
        job.PublishedAt = DateTimeOffset.UtcNow;
        return job;
    }

    [Fact]
    public async Task Creating_a_draft_does_not_sync()
    {
        var (service, _, outbox) = Build();
        await service.CreateAsync(Input());
        Assert.Empty(outbox.VacancySyncs);
    }

    [Fact]
    public async Task Publishing_syncs_the_job()
    {
        var (service, _, outbox) = Build(DraftJob());
        await service.PublishAsync(1);
        Assert.Equal(JobStatus.Published, Assert.Single(outbox.VacancySyncs).Status);
    }

    [Fact]
    public async Task Closing_syncs_the_job()
    {
        var (service, _, outbox) = Build(PublishedJob());
        await service.CloseAsync(1);
        Assert.Equal(JobStatus.Closed, Assert.Single(outbox.VacancySyncs).Status);
    }

    [Fact]
    public async Task Editing_a_never_published_draft_does_not_sync()
    {
        var (service, _, outbox) = Build(DraftJob());
        await service.UpdateAsync(Input(id: 1));
        Assert.Empty(outbox.VacancySyncs);
    }

    [Fact]
    public async Task Editing_a_published_job_syncs_the_new_title()
    {
        var (service, _, outbox) = Build(PublishedJob());
        await service.UpdateAsync(Input(id: 1, title: "Lead Developer"));
        Assert.Equal("Lead Developer", Assert.Single(outbox.VacancySyncs).Title);
    }

    [Fact]
    public async Task Deleting_a_published_job_syncs_it_as_deleted()
    {
        var (service, _, outbox) = Build(PublishedJob());
        await service.DeleteAsync(1);
        Assert.True(Assert.Single(outbox.VacancySyncs).IsDeleted);
    }

    [Fact]
    public async Task Deleting_a_never_published_draft_does_not_sync()
    {
        var (service, _, outbox) = Build(DraftJob());
        await service.DeleteAsync(1);
        Assert.Empty(outbox.VacancySyncs);
    }

    [Fact]
    public async Task A_rejected_transition_does_not_sync()
    {
        var (service, _, outbox) = Build(DraftJob());
        await service.CloseAsync(1);   // only a published job can be closed
        Assert.Empty(outbox.VacancySyncs);
    }
```
- [x] **Step 4:** `dotnet test Ats.slnx --filter "FullyQualifiedName~JobServiceTests"`. Expected: build FAIL (ctor).
- [x] **Step 5: Implement `JobService`.** Constructor:
```csharp
    private readonly IJobRepository _repo;
    private readonly IOutboxEnqueuer _outbox;
    public JobService(IJobRepository repo, IOutboxEnqueuer outbox)
    {
        _repo = repo; _outbox = outbox;
    }
```
  (add `using Ats.Application.Integration;`). Immediately before the `SaveChangesAsync` call:
  - `UpdateAsync`: `if (job.PublishedAt is not null) await _outbox.StageVacancySyncAsync(job, ct);`
  - `PublishAsync`: `await _outbox.StageVacancySyncAsync(job, ct);`
  - `CloseAsync`: `await _outbox.StageVacancySyncAsync(job, ct);`
  - `DeleteAsync`: `if (job.PublishedAt is not null) await _outbox.StageVacancySyncAsync(job, ct);`
- [x] **Step 6:** `IntegrationOptions` add:
```csharp
    // Public career-site root used in the vacancy URL ReferralTool shows referrers. One host per environment.
    public string CareerSiteBaseUrl { get; set; } = string.Empty;
```
- [x] **Step 7: Implement `OutboxEnqueuer.StageVacancySyncAsync`.** Constructor becomes
  `OutboxEnqueuer(AtsDbContext db, IOptions<IntegrationOptions> opts)` storing `_opts = opts.Value`; add
  `using System.Text.Json; using Microsoft.Extensions.Options;`:
```csharp
    public async Task StageVacancySyncAsync(Job job, CancellationToken ct = default)
    {
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(ct);
        if (settings is null || !settings.IntegrationEnabled || settings.ReferralToolCustomerId is null) return;

        // Tenant is not an ITenantEntity; reading the job's own tenant row by id is not a filter bypass.
        var slug = await _db.Tenants.Where(t => t.Id == job.TenantId).Select(t => t.Slug).FirstAsync(ct);
        var location = job.LocationId is int locationId
            ? await _db.Locations.Where(l => l.Id == locationId).Select(l => l.City ?? l.Name).FirstOrDefaultAsync(ct)
            : null;
        var department = job.DepartmentId is int departmentId
            ? await _db.Departments.Where(d => d.Id == departmentId).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;

        await _db.OutboxMessages.AddAsync(new OutboxMessage
        {
            Kind = OutboxKind.VacancySync,
            JobId = job.Id,
            ExternalVacancyId = job.ExternalRef,
            Payload = JsonSerializer.Serialize(VacancyPayload.From(job, _opts.CareerSiteBaseUrl, slug, location, department)),
            Status = OutboxStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow
        }, ct);
        // No SaveChanges: the caller commits this with the job change.
    }
```
- [x] **Step 8:** `src/Ats.Web/Program.cs`, after `AddAtsInfrastructure`:
```csharp
// Vacancy snapshots carry an absolute career-site URL; fail at startup rather than on the first publish.
builder.Services.AddOptions<IntegrationOptions>()
    .Bind(builder.Configuration.GetSection("Integration"))
    .Validate(o => Uri.TryCreate(o.CareerSiteBaseUrl, UriKind.Absolute, out var u) && u.Scheme is "https" or "http",
        "Integration:CareerSiteBaseUrl must be an absolute http(s) URL.")
    .ValidateOnStart();
```
  and in `src/Ats.Web/appsettings.Development.json` add `"Integration": { "CareerSiteBaseUrl": "https://localhost:7044" }`.
  Confirm 7044 against `src/Ats.Web/Properties/launchSettings.json` and use the https profile's port.
- [x] **Step 9:** `dotnet test Ats.slnx` green; mutation-check: remove the `PublishedAt` guard in `DeleteAsync`,
  confirm `Deleting_a_never_published_draft_does_not_sync` fails, restore. Build warning-clean, format clean.

### Task 5: Deliver vacancy syncs (client + worker)

**Files:** Modify `src/Ats.Application/Integration/IReferralToolClient.cs`,
`src/Ats.Infrastructure/Integration/ReferralToolClient.cs`, `src/Ats.Infrastructure/Integration/OutboxProcessor.cs`,
`src/Ats.Application/Integration/OutboxProcessing.cs` (comment), `src/Ats.Web/Views/Integration/_DeliveryRows.cshtml`

- [x] **Step 1:** `IReferralToolClient` add:
```csharp
    // POST /v1.0/vacancy (X-Api-Key identifies the customer). ReferralTool rejects an existing Id with 400.
    Task<ReferralCallResult> CreateVacancyAsync(ReferralToolSettings settings, VacancyPayload vacancy, CancellationToken ct = default);

    // PUT /v1.0/vacancy/{id}: a full replace, so every field is always sent.
    Task<ReferralCallResult> UpdateVacancyAsync(ReferralToolSettings settings, VacancyPayload vacancy, CancellationToken ct = default);
```
- [x] **Step 2:** `ReferralToolClient`: generalise the private helpers to take method and path, keep the Kafka calls:
```csharp
    public async Task<(ReferralCallResult Result, bool? Exists)> CheckVacancyExistsAsync(
        ReferralToolSettings settings, string externalVacancyId, CancellationToken ct = default)
    {
        var result = await SendAsync(settings, HttpMethod.Post, "kafka/checkvacancyexists",
            new { CustomerId = settings.CustomerId, ExternalVacancyId = externalVacancyId }, ct);
        var exists = result.Reached && result.HttpStatus is >= 200 and < 300
            ? ReferralToolRules.ReadVacancyExists(result.Body)
            : null;
        return (result, exists);
    }

    public Task<ReferralCallResult> SendStatusUpdateAsync(
        ReferralToolSettings settings, StatusUpdateRequest r, CancellationToken ct = default) =>
        SendAsync(settings, HttpMethod.Post, "kafka/candidatestatusupdate",
            new { r.CustomerId, r.Code, r.ExternalVacancyId, r.ExternalCandidateId, r.CandidateStatus }, ct);

    public Task<ReferralCallResult> CreateVacancyAsync(
        ReferralToolSettings settings, VacancyPayload v, CancellationToken ct = default) =>
        SendAsync(settings, HttpMethod.Post, "vacancy",
            new { v.Id, v.Title, v.Url, v.Location, v.EmploymentType, v.Categories }, ct);

    public Task<ReferralCallResult> UpdateVacancyAsync(
        ReferralToolSettings settings, VacancyPayload v, CancellationToken ct = default) =>
        SendAsync(settings, HttpMethod.Put, $"vacancy/{Uri.EscapeDataString(v.Id)}",
            new { v.Title, v.Url, v.Inactive, v.Location, v.EmploymentType, v.Categories }, ct);
```
  Rename `PostAsync(settings, action, payload, ct)` to `SendAsync(settings, method, path, payload, ct)` and
  `Build` to:
```csharp
    private static HttpRequestMessage Build(ReferralToolSettings s, HttpMethod method, string path, object payload)
    {
        var request = new HttpRequestMessage(method, $"{s.BaseUrl.TrimEnd('/')}/v1.0/{path}");
        request.Headers.TryAddWithoutValidation("X-Api-Key", s.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Auth-Token", s.AuthToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
```
- [x] **Step 3:** `OutboxProcessor.ProcessAsync`: replace the two lines
```csharp
        if (exists is null)
            return await DeferAsync(msg, "Vacancy check returned an unreadable response.", ct);
        if (exists == false)
            return await DeferAsync(msg, "Vacancy not imported yet.", ct);
```
  with
```csharp
        if (exists is null)
            return await DeferAsync(msg, "Vacancy check returned an unreadable response.", ct);
        if (msg.Kind == OutboxKind.VacancySync)
            return await SyncVacancyAsync(msg, settings, exists.Value, ct);
        if (exists == false)
            return await DeferAsync(msg, "Vacancy not in ReferralTool yet.", ct);
```
  and add (add `using System.Text.Json;`):
```csharp
    // Idempotent by construction: existence is re-checked on every attempt, so a POST that timed out after
    // ReferralTool stored it becomes a PUT next time. No pre-send intent row is needed (unlike status updates).
    private async Task<OutboxOutcome> SyncVacancyAsync(
        OutboxMessage msg, ReferralToolSettings settings, bool exists, CancellationToken ct)
    {
        var vacancy = JsonSerializer.Deserialize<VacancyPayload>(msg.Payload!)!;
        var call = ReferralToolRules.DecideVacancyCall(exists, vacancy.Inactive);
        if (call == VacancyCall.None)
            return await FinishAsync(msg, OutboxStatus.Delivered, null);

        var attempt = Log(msg.Id, call == VacancyCall.Create ? DeliveryKind.VacancyCreate : DeliveryKind.VacancyUpdate);
        var result = call == VacancyCall.Create
            ? await _client.CreateVacancyAsync(settings, vacancy, ct)
            : await _client.UpdateVacancyAsync(settings, vacancy, ct);
        var outcome = ReferralToolRules.ClassifyVacancyCall(result.Reached, result.HttpStatus, result.Body);
        Record(attempt, result, outcome == OutboxOutcome.Delivered);
        await _db.SaveChangesAsync(CancellationToken.None);

        return outcome switch
        {
            OutboxOutcome.Delivered => await FinishAsync(msg, OutboxStatus.Delivered, null),
            OutboxOutcome.Postpone => await PostponeAsync(msg, CredentialsRejected(result.HttpStatus), CancellationToken.None),
            OutboxOutcome.Failed => await FinishAsync(msg, OutboxStatus.Failed, Trunc($"{result.HttpStatus}: {result.Body}", 1000)),
            _ => await DeferAsync(msg, $"Vacancy {call} failed ({result.HttpStatus}).", ct)
        };
    }

    private async Task<OutboxOutcome> FinishAsync(OutboxMessage msg, OutboxStatus status, string? error)
    {
        msg.Status = status;
        msg.LastError = error;
        await _db.SaveChangesAsync(CancellationToken.None);
        return status == OutboxStatus.Delivered ? OutboxOutcome.Delivered : OutboxOutcome.Failed;
    }
```
  Then use `FinishAsync` in the status-update path for the Delivered and Failed endings (same behaviour,
  less duplication). Update the `IOutboxProcessor.ProcessAsync` comment to mention vacancy syncs.
- [x] **Step 4:** `_DeliveryRows.cshtml`: add `@using Ats.Domain.Enums` is present; change the header span
  `Status sent` to `Sent`, the cell `<span class="ats-small">@e.Message.CandidateStatus</span>` to
  `<span class="ats-small">@(e.Message.Kind == OutboxKind.VacancySync ? "Vacancy sync" : e.Message.CandidateStatus)</span>`,
  and the empty-state text to `"No deliveries yet."`.
- [x] **Step 5:** `dotnet build Ats.slnx`, `dotnet test Ats.slnx`, `dotnet format Ats.slnx --verify-no-changes`.

### Task 6: Integration screen - vacancy sync card

**Files:** Modify `src/Ats.Application/Integration/IIntegrationSettingsService.cs`,
`src/Ats.Infrastructure/Integration/IntegrationSettingsService.cs`, `src/Ats.Web/Controllers/IntegrationController.cs`,
`src/Ats.Web/Models/IntegrationSettingsViewModel.cs`, `src/Ats.Web/Views/Integration/Index.cshtml`

- [x] **Step 1:** Interface: remove `GenerateFeedKeyAsync` and its comment; add
```csharp
    // Published jobs, shown on the vacancy card.
    Task<int> CountPublishedJobsAsync(CancellationToken ct = default);

    // Stages a vacancy sync for every non-draft job (first switch-on, or after the integration was off).
    // Returns how many were queued; 0 when the integration is disabled.
    Task<int> QueueVacancySyncAsync(CancellationToken ct = default);
```
- [x] **Step 2:** Service: drop `IVacancyFeedRepository` from the constructor, remove `GenerateFeedKeyAsync`, inject
  `IOutboxEnqueuer outbox` (field `_outbox`), and add:
```csharp
    public Task<int> CountPublishedJobsAsync(CancellationToken ct = default) =>
        _db.Jobs.CountAsync(j => j.Status == JobStatus.Published, ct);

    public async Task<int> QueueVacancySyncAsync(CancellationToken ct = default)
    {
        var s = await _db.TenantSettings.AsNoTracking().FirstAsync(ct);
        if (ReferralToolRules.SettingsProblem(s) is not null) return 0;

        var jobs = await _db.Jobs.Where(j => j.Status != JobStatus.Draft).ToListAsync(ct);
        foreach (var job in jobs)
            await _outbox.StageVacancySyncAsync(job, ct);
        await _db.SaveChangesAsync(ct);
        return jobs.Count;
    }
```
  In `TestConnectionAsync` replace the two feed lines with
```csharp
        var sampleRef = await _db.Jobs.AsNoTracking()
            .Where(j => j.Status != JobStatus.Draft).OrderBy(j => j.Id)
            .Select(j => j.ExternalRef).FirstOrDefaultAsync(ct);
```
- [x] **Step 3:** Controller: drop `IVacancyFeedRepository`; `PublishedJobCount = await _settings.CountPublishedJobsAsync()`;
  remove `FeedLastPulledAt` and `HasFeedKey` assignments; replace `GenerateFeedKey` with
```csharp
    [HttpPost]
    public async Task<IActionResult> SyncVacancies()
    {
        var queued = await _settings.QueueVacancySyncAsync();
        if (queued > 0)
            await _audit.LogAsync("VacancySyncQueued", "TenantSettings", null, $"Queued {queued} vacancy sync(s)");
        TempData[queued > 0 ? "Success" : "Error"] = queued > 0
            ? $"Queued {queued} vacancy sync(s) to ReferralTool."
            : "Nothing queued: enable and complete the integration settings first.";
        return RedirectToAction(nameof(Index));
    }
```
- [x] **Step 4:** View model: remove `HasFeedKey` and `IntegrationHealthViewModel.FeedLastPulledAt`.
- [x] **Step 5:** View: remove the `lastPull` variable, the `TempData["FeedKey"]` alert and the `· feed ...` line;
  replace the whole right-hand card (`<div class="col-lg-5">` contents) with
```cshtml
        <div class="ats-card d-flex flex-column gap-3">
            <div><span class="ats-eyebrow">Vacancies:</span><h2 class="mt-1 mb-0">Pushed to ReferralTool.</h2></div>
            <p class="ats-small ats-muted mb-0">Publishing, editing, closing or deleting a job sends it to ReferralTool automatically. Closed and deleted jobs are marked inactive there.</p>
            <div class="d-flex align-items-center justify-content-between">
                <span class="ats-small">Published jobs</span><strong class="ats-stat-mini">@Model.PublishedJobCount</strong>
            </div>
            <form asp-action="SyncVacancies" method="post"
                  hx-confirm="Send every published and closed job to ReferralTool now?">
                <button type="submit" class="btn btn-outline-secondary btn-sm"><span class="ms ms-sm">sync</span> Sync vacancies now</button>
            </form>
        </div>
```
  Keep `hx-confirm` (boosted form), no inline grid columns, no raw hex.
- [x] **Step 6:** Build, test, format. UI change: run `e2e-verifier` (integration, a11y, layout specs).

### Task 7: Remove the pull feed and `Ats.Api`

**Files:** Delete `src/Ats.Api/` (whole folder), `src/Ats.Application/Integration/FeedApiKey.cs`,
`src/Ats.Application/Common/FeedPullThrottle.cs`, `src/Ats.Application/Integration/IVacancyFeedRepository.cs`,
`src/Ats.Infrastructure/Persistence/Repositories/VacancyFeedRepository.cs`, `tests/Ats.Tests/Integration/FeedPullThrottleTests.cs`.
Modify `Ats.slnx`, `src/Ats.Infrastructure/DependencyInjection.cs`, `src/Ats.Application/Dashboard/DashboardSummary.cs`,
`src/Ats.Infrastructure/Dashboard/DashboardService.cs`, `src/Ats.Web/Views/Dashboard/Index.cshtml`,
`src/Ats.Domain/Entities/TenantSettings.cs`, `tests/e2e/security.spec.ts`.

- [x] **Step 1:** Delete the files above; remove `<Project Path="src/Ats.Api/Ats.Api.csproj" />` from `Ats.slnx`;
  remove `services.AddScoped<IVacancyFeedRepository, VacancyFeedRepository>();` from DI.
- [x] **Step 2:** Remove the `FeedLastPulledAt` parameter from the dashboard integration record in
  `DashboardSummary.cs`, its argument in `DashboardService.cs:110`, and the feed-pull sentence in
  `Views/Dashboard/Index.cshtml:93-95` (keep the surrounding card markup valid).
- [x] **Step 3:** `TenantSettings`: keep `FeedApiKeyHash` and `FeedLastPulledAt` (no migration now) with the comment
  `// Unused since the vacancy push (2026-09-28); dropped in a follow-up migration once rollback is no longer needed.`
  replacing the telemetry comment.
- [x] **Step 4:** `tests/e2e/security.spec.ts`: delete the two `the vacancy feed rejects ...` tests.
- [x] **Step 5:** `grep -rn "Feed\|Ats.Api" src tests --include=*.cs --include=*.cshtml --include=*.ts --include=*.slnx`
  (excluding `Migrations/`, `bin/`, `obj/`) returns only the two kept `TenantSettings` properties.
- [x] **Step 6:** Build, test, format; `e2e-verifier` on dashboard + security specs.

### Task 8: Documentation (phase close-out)

**Files:** `.claude/skills/integration/SKILL.md`, `.claude/skills/multitenancy/SKILL.md`, `.claude/rules/multi-tenancy.md`,
`.claude/skills/architecture/SKILL.md`, `CLAUDE.md`

- [x] **Step 1:** Integration skill: replace "Vacancy feed (Ats.Api)", "Feed pull telemetry" and the ReferralTool-side
  setup step 1 with a "Vacancy push" section: triggers (JobService publish/update-if-ever-published/close/
  delete-if-ever-published), `VacancyPayload` snapshot, per-job claim ordering, exists -> PUT / POST / none,
  never DELETE, `ClassifyVacancyCall`, `Integration:CareerSiteBaseUrl` (Web, validated on start), "Sync
  vacancies now", and the ReferralTool prerequisite `ImportSettings.Enabled = 0`. Setup list: credentials
  copied from ReferralTool (`ApiKeys` GUID, `Kafka:AuthToken`).
- [x] **Step 2:** Multi-tenancy rule + skill: remove the `FeedApiKeyFilter` bullet; `Items["TenantId"]` is now set in
  one place (`TenantResolutionMiddleware`); keep the count of documented bypass spots consistent.
- [x] **Step 3:** Architecture skill and `CLAUDE.md`: remove the `Ats.Api` row and mentions; the integration skill-index
  row now reads "Vacancy push, outbox, worker, ReferralTool client, settings".
- [x] **Step 4:** `grep -rn "Ats.Api\|jobs/search\|feed key" .claude CLAUDE.md` returns nothing (old dated plans/specs
  under `docs/` stay as history).

### Task 9: Development-only switch to allow a local/http ReferralTool base URL

Added 2026-09-29 at the developer's request, to test against a ReferralTool running on this machine.
`ReferralToolBaseUrl.Validate` (SEC-6) rejects http and localhost/private hosts. Add
`IntegrationOptions.AllowInsecureReferralToolUrl` (default false), honoured only in Development (both hosts
refuse to start when it is true elsewhere), set true in `appsettings.Development.json` of Ats.Web and Ats.Worker.

- [x] `Validate(value, allowInsecure = false)`: when true, accept http and private/localhost hosts (still an absolute http(s) URL).
- [x] Thread an optional `allowInsecure = false` through `ReferralToolRules.ConnectionProblem`, `SettingsProblem`, `BlockedReason`.
- [x] Pass the option from `IntegrationSettingsService` (save + test + sync), `OutboxProcessor`, `DashboardService`, `ShellSummaryService`.
- [x] Startup guard in Web and Worker: true outside Development fails start.
- [x] Tests for the allowed/rejected cases with the flag on and off. Docs: integration skill + SEC-6 note.

## Follow-up (not in this plan)
- Migration dropping `TenantSettings.FeedApiKeyHash` and `FeedLastPulledAt` after one release.
- Refresh `D:\Documents\ATS-ReferralTool-Integration-Guide.docx` sections 5, 7, 8, 10, 13.

## Known risk (accepted, ReferralTool unchanged)
ReferralTool's Kafka endpoints trust `CustomerId` from the body rather than the X-Api-Key's customer, and
`X-Auth-Token` is one global value. Anyone holding a valid API key and the token can post candidate events for
another customer. Keep both secrets restricted to operators.
