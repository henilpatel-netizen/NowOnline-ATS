using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class Candidate : TenantEntity, ISoftDeletable
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ResumeFileKey { get; set; }   // set in Phase 2
    public bool IsDeleted { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
