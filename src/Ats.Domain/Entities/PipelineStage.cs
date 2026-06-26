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
