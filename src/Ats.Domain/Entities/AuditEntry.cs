using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class AuditEntry : TenantEntity
{
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityRef { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}
