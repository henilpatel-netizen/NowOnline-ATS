namespace Ats.Application.Abstractions;

public interface ITenantContext
{
    // null only during onboarding / unauthenticated public requests with no slug
    int? CurrentTenantId { get; }
    bool HasTenant { get; }
}
