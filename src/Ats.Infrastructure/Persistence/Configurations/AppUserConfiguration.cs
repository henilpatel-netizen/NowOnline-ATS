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
        // Email is globally unique across all tenants: one email maps to exactly one user in exactly
        // one tenant, so back-office sign-in resolves deterministically (see IdentityService).
        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");
    }
}
