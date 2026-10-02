using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).IsRequired().HasMaxLength(256);
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.Role).IsRequired().HasMaxLength(40);
        // Existing users stay active. Sentinel true: EF omits the value when it is true (the database default
        // applies) and sends false explicitly, which avoids EF's "bool with a non-false default" warning.
        b.Property(u => u.IsActive).HasDefaultValue(true).HasSentinel(true);
        // Existing rows get a fresh stamp from the database; new rows get one from the entity initialiser.
        b.Property(u => u.SecurityStamp).HasDefaultValueSql("NEWID()");
        // Email is globally unique across all tenants: one email maps to exactly one user in exactly
        // one tenant, so back-office sign-in resolves deterministically (see IdentityService).
        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");
        // Keeps the serializable last-Owner count's range locks inside one tenant instead of the whole table.
        b.HasIndex(u => new { u.TenantId, u.Role, u.IsActive }).HasDatabaseName("IX_Users_TenantId_Role_IsActive");
    }
}
