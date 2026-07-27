using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

// Named JobApplication (not Application) to avoid colliding with the Ats.Application namespace.
// The DbSet is still named Applications, so the table name remains "Applications".
public class JobApplication : TenantEntity, ISoftDeletable
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
