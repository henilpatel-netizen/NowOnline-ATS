namespace Ats.Application.Tenancy;

public record RegisterTenantInput(
    string CompanyName,
    string Slug,
    string OwnerName,
    string OwnerEmail,
    string Password);
