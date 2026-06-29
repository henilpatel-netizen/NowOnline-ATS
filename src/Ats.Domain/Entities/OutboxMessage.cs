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
