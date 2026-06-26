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
    public DateTimeOffset CreatedAt { get; set; }
}
