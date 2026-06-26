namespace Ats.Domain.Common;

public abstract class TenantEntity : KeyedEntity, ITenantEntity
{
    public int TenantId { get; set; }
}
