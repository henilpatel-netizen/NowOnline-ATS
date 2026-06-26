using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.HasKey(t => t.Id);
        b.HasAlternateKey(t => t.Key);
        b.Property(t => t.Name).IsRequired().HasMaxLength(200);
        b.Property(t => t.Slug).IsRequired().HasMaxLength(60);
        b.HasIndex(t => t.Slug).IsUnique();
        b.HasOne(t => t.Settings).WithOne().HasForeignKey<TenantSettings>(s => s.TenantId);
    }
}
