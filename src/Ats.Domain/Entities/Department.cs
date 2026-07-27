using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class Department : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}
