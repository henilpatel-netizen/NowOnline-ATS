using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class WebhookDelivery : TenantEntity
{
    public int OutboxMessageId { get; set; }
    public DeliveryKind Kind { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public int? HttpStatus { get; set; }
    public string? ResponseBody { get; set; }
    public bool Success { get; set; }
}
