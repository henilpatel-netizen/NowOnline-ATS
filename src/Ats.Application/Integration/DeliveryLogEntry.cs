using Ats.Domain.Entities;

namespace Ats.Application.Integration;

public sealed record DeliveryLogEntry(OutboxMessage Message, IReadOnlyList<WebhookDelivery> Deliveries);
