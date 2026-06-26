namespace Ats.Application.Tenancy;

public record RegisterTenantResult(bool Succeeded, int TenantId, int OwnerUserId, string? Error);
