using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class Location : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
}
