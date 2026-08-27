using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class PipelineTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public List<PipelineStage> Stages { get; set; } = new();

    // Optimistic-concurrency token: two admins editing the same template no longer silently clobber
    // each other (the second save is rejected with a reload prompt).
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
