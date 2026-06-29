using Ats.Domain.Entities;

namespace Ats.Application.Pipelines;

public record StageInput(
    int? Id, string Name, int Order, bool IsTerminal, StageOutcome TerminalOutcome,
    string? ReferralStatusOverride, bool Delete);

public record PipelineTemplateInput(int? Id, string Name, List<StageInput> Stages);
