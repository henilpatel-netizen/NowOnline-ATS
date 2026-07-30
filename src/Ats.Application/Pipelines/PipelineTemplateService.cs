using Ats.Application.Departments; // OperationResult
using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public interface IPipelineTemplateService
{
    Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default);
    Task<Dictionary<int, int>> JobCountsByTemplateAsync(CancellationToken ct = default);
    Task<PipelineTemplate?> GetAsync(int id, CancellationToken ct = default);
    Task<OperationResult> SaveAsync(PipelineTemplateInput input, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class PipelineTemplateService : IPipelineTemplateService
{
    private readonly IPipelineTemplateRepository _repo;
    public PipelineTemplateService(IPipelineTemplateRepository repo) => _repo = repo;

    public Task<List<PipelineTemplate>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<Dictionary<int, int>> JobCountsByTemplateAsync(CancellationToken ct = default) => _repo.JobCountsByTemplateAsync(ct);
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
