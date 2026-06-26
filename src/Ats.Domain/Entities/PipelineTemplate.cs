using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class PipelineTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public List<PipelineStage> Stages { get; set; } = new();
}
