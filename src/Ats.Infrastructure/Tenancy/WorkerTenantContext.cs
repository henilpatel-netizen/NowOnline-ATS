using Ats.Application.Abstractions;

namespace Ats.Infrastructure.Tenancy;

// Settable tenant context for the worker (no HttpContext). The drainer sets it per message so the
// global query filter, TenantId stamping, and WebhookDelivery insert all scope to that tenant.
public sealed class WorkerTenantContext : ITenantContext
{
    public int? CurrentTenantId { get; set; }
    public bool HasTenant => CurrentTenantId is not null;
}
