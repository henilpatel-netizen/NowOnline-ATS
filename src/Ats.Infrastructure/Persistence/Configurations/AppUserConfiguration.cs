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
        // email unique per tenant
        b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
    }
}
