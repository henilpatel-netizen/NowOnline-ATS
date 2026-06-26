using Ats.Domain.Common;
using Ats.Domain.Enums;

namespace Ats.Domain.Entities;

public class Tenant : KeyedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public TenantSettings? Settings { get; set; }
}
