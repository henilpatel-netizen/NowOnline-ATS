using Ats.Domain.Common;

namespace Ats.Domain.Entities;

public class AppUser : ITenantEntity
{
    public int Id { get; set; }
    public Guid Key { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;   // one of AtsRole.*
    public bool IsActive { get; set; } = true;
    // Set for a temporary password (new user or admin reset); cleared when the user sets their own.
    public bool MustChangePassword { get; set; }
    // Rotated on every security-relevant change; a cookie carrying an older value is rejected.
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
}
