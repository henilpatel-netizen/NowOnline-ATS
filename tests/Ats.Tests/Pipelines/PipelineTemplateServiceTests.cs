using Ats.Application.Pipelines;
using Ats.Domain.Entities;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Pipelines;

// The add / rename / reorder / remove diff in SaveAsync had no coverage (QUAL-1). It decides what
// happens to stages that candidates are already sitting in, so a regression here is expensive.
public class PipelineTemplateServiceTests
{
    private const int TemplateId = 5;

    private static (PipelineTemplateService Service, FakePipelineTemplateRepository Repo) Build(
        params PipelineStage[] existingStages)
    {
        var repo = new FakePipelineTemplateRepository();
        if (existingStages.Length > 0)
        {
            repo.Templates.Add(new PipelineTemplate
            {
                Id = TemplateId,
                Name = "Standard",
                Stages = existingStages.ToList()
            });
        }
        return (new PipelineTemplateService(repo), repo);
    }

    private static StageInput Stage(int? id, string name, int order, bool terminal = false,
                                   StageOutcome outcome = StageOutcome.None, string? referral = null,
                                   bool delete = false) =>
        new(id, name, order, terminal, outcome, referral, delete);

    // ---- Validation ---------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_template_needs_a_name(string name)
    {
        var (service, _) = Build();

        var result = await service.SaveAsync(new PipelineTemplateInput(null, name, new() { Stage(null, "Applied", 1) }));

        Assert.False(result.Succeeded);
        Assert.Contains("name is required", result.Error);
    }

    [Fact]
    public async Task A_template_needs_at_least_one_surviving_stage()
    {
        var (service, _) = Build(new PipelineStage { Id = 1, Name = "Applied", Order = 1 });

        // The only stage is flagged for deletion.
        var result = await service.SaveAsync(new PipelineTemplateInput(
            TemplateId, "Standard", new() { Stage(1, "Applied", 1, delete: true) }));

        Assert.False(result.Succeeded);
        Assert.Contains("at least one stage", result.Error);
    }

    [Fact]
    public async Task Editing_an_unknown_template_is_rejected()
    {
        var (service, _) = Build();   // no templates seeded

        var result = await service.SaveAsync(new PipelineTemplateInput(
            9999, "Standard", new() { Stage(null, "Applied", 1) }));

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
    }

    // ---- Create -------------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_template_adds_it_with_its_stages()
    {
        var (service, repo) = Build();

        var result = await service.SaveAsync(new PipelineTemplateInput(null, "  Fresh  ", new()
        {
            Stage(null, " Applied ", 1),
            Stage(null, "Hired", 2, terminal: true, outcome: StageOutcome.Hired)
        }));

        Assert.True(result.Succeeded);
        Assert.True(repo.AddCalled);
        var created = Assert.Single(repo.Templates);
        Assert.Equal("Fresh", created.Name);                 // trimmed
        Assert.Equal(2, created.Stages.Count);
        Assert.Equal("Applied", created.Stages[0].Name);      // trimmed
        Assert.Equal(StageOutcome.Hired, created.Stages[1].TerminalOutcome);
    }

    // ---- The stage diff -----------------------------------------------------------------------

    [Fact]
    public async Task An_existing_stage_is_renamed_in_place_not_replaced()
    {
        var existing = new PipelineStage { Id = 1, Name = "Applied", Order = 1 };
        var (service, repo) = Build(existing);

        var result = await service.SaveAsync(new PipelineTemplateInput(
            TemplateId, "Standard", new() { Stage(1, "Screening", 1) }));

        Assert.True(result.Succeeded);
        var template = repo.Templates.Single();
        var stage = Assert.Single(template.Stages);
        Assert.Same(existing, stage);            // same row: candidates in it are unaffected
        Assert.Equal("Screening", stage.Name);
        Assert.Empty(repo.RemovedStages);
    }

    [Fact]
    public async Task A_stage_flagged_for_deletion_is_removed()
    {
        var keep = new PipelineStage { Id = 1, Name = "Applied", Order = 1 };
        var drop = new PipelineStage { Id = 2, Name = "Obsolete", Order = 2 };
        var (service, repo) = Build(keep, drop);

        var result = await service.SaveAsync(new PipelineTemplateInput(TemplateId, "Standard", new()
        {
            Stage(1, "Applied", 1),
            Stage(2, "Obsolete", 2, delete: true)
        }));

        Assert.True(result.Succeeded);
        Assert.Same(drop, Assert.Single(repo.RemovedStages));
        Assert.Same(keep, Assert.Single(repo.Templates.Single().Stages));
    }

    [Fact]
    public async Task A_stage_omitted_from_the_post_is_also_removed()
    {
        // The editor can drop a row entirely rather than flagging it; both must delete.
        var keep = new PipelineStage { Id = 1, Name = "Applied", Order = 1 };
        var gone = new PipelineStage { Id = 2, Name = "Vanished", Order = 2 };
        var (service, repo) = Build(keep, gone);

        var result = await service.SaveAsync(new PipelineTemplateInput(
            TemplateId, "Standard", new() { Stage(1, "Applied", 1) }));

        Assert.True(result.Succeeded);
        Assert.Same(gone, Assert.Single(repo.RemovedStages));
    }

    [Fact]
    public async Task A_new_stage_is_appended_to_an_existing_template()
    {
        var existing = new PipelineStage { Id = 1, Name = "Applied", Order = 1 };
        var (service, repo) = Build(existing);

        var result = await service.SaveAsync(new PipelineTemplateInput(TemplateId, "Standard", new()
        {
            Stage(1, "Applied", 1),
            Stage(null, "Interview", 2)          // no id = new
        }));

        Assert.True(result.Succeeded);
        var stages = repo.Templates.Single().Stages;
        Assert.Equal(2, stages.Count);
        Assert.Contains(stages, s => s.Name == "Interview" && s.Id == 0);
    }

    [Fact]
    public async Task Reordering_updates_the_order_of_the_same_rows()
    {
        var a = new PipelineStage { Id = 1, Name = "Applied", Order = 1 };
        var b = new PipelineStage { Id = 2, Name = "Interview", Order = 2 };
        var (service, repo) = Build(a, b);

        var result = await service.SaveAsync(new PipelineTemplateInput(TemplateId, "Standard", new()
        {
            Stage(1, "Applied", 2),
            Stage(2, "Interview", 1)
        }));

        Assert.True(result.Succeeded);
        Assert.Equal(2, a.Order);
        Assert.Equal(1, b.Order);
        Assert.Empty(repo.RemovedStages);
    }

    [Fact]
    public async Task Clearing_the_terminal_flag_also_clears_the_outcome()
    {
        // Otherwise a non-terminal stage would keep a Hired/Rejected outcome and corrupt the
        // dashboard metrics and status mapping.
        var stage = new PipelineStage
        {
            Id = 1,
            Name = "Hired",
            Order = 1,
            IsTerminal = true,
            TerminalOutcome = StageOutcome.Hired
        };
        var (service, _) = Build(stage);

        await service.SaveAsync(new PipelineTemplateInput(TemplateId, "Standard", new()
        {
            Stage(1, "Hired", 1, terminal: false, outcome: StageOutcome.Hired)
        }));

        Assert.False(stage.IsTerminal);
        Assert.Equal(StageOutcome.None, stage.TerminalOutcome);
    }

    [Fact]
    public async Task A_blank_referral_override_is_stored_as_null()
    {
        // Null means "use the stage name" downstream; an empty string would be sent to ReferralTool.
        var stage = new PipelineStage { Id = 1, Name = "Applied", Order = 1, ReferralStatusOverride = "old" };
        var (service, _) = Build(stage);

        await service.SaveAsync(new PipelineTemplateInput(TemplateId, "Standard", new()
        {
            Stage(1, "Applied", 1, referral: "   ")
        }));

        Assert.Null(stage.ReferralStatusOverride);
    }

    // ---- Concurrency (DATA-8) -----------------------------------------------------------------

    [Fact]
    public async Task A_concurrent_template_edit_is_reported_not_silently_overwritten()
    {
        var (service, repo) = Build(new PipelineStage { Id = 1, Name = "Applied", Order = 1 });
        repo.ConcurrencyConflict = true;

        var result = await service.SaveAsync(new PipelineTemplateInput(
            TemplateId, "Standard", new() { Stage(1, "Applied", 1) }, new byte[] { 1, 2, 3 }));

        Assert.False(result.Succeeded);
        Assert.Contains("changed by someone else", result.Error);
    }

    [Fact]
    public async Task The_supplied_row_version_is_pinned_when_present()
    {
        var (service, repo) = Build(new PipelineStage { Id = 1, Name = "Applied", Order = 1 });
        var token = new byte[] { 7, 7 };

        await service.SaveAsync(new PipelineTemplateInput(
            TemplateId, "Standard", new() { Stage(1, "Applied", 1) }, token));

        Assert.Equal(token, repo.ExpectedRowVersion);
    }

    // ---- Delete -------------------------------------------------------------------------------

    [Fact]
    public async Task A_template_in_use_by_a_job_cannot_be_deleted()
    {
        var (service, repo) = Build(new PipelineStage { Id = 1, Name = "Applied", Order = 1 });
        repo.UsedByJob = true;

        var result = await service.DeleteAsync(TemplateId);

        Assert.False(result.Succeeded);
        Assert.Contains("used by one or more jobs", result.Error);
        Assert.Empty(repo.RemovedTemplates);
    }

    [Fact]
    public async Task An_unused_template_is_deleted()
    {
        var (service, repo) = Build(new PipelineStage { Id = 1, Name = "Applied", Order = 1 });

        var result = await service.DeleteAsync(TemplateId);

        Assert.True(result.Succeeded);
        Assert.Single(repo.RemovedTemplates);
    }

    [Fact]
    public async Task Deleting_an_unknown_template_is_rejected()
    {
        var (service, _) = Build();

        var result = await service.DeleteAsync(123);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
    }
}
