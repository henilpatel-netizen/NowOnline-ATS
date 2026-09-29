using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class OutboxMessage : TenantEntity
{
    public int? ApplicationId { get; set; }
    public OutboxKind Kind { get; set; } = OutboxKind.CandidateStatus;
    public int? JobId { get; set; }
    // VacancySync only: the VacancyPayload JSON snapshot.
    public string? Payload { get; set; }
    // CandidateStatus payload snapshot (empty for VacancySync).
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
