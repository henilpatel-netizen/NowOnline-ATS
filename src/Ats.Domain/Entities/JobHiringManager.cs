using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class JobHiringManager : TenantEntity
{
    public int JobId { get; set; }
    public int UserId { get; set; }
}
