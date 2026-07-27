using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class ApplicationEvent : TenantEntity
{
    public int ApplicationId { get; set; }
    public int? FromStageId { get; set; }
    public int ToStageId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? MovedByUserId { get; set; }
}
