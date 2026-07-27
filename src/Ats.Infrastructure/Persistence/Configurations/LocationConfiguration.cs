using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.Name).IsRequired().HasMaxLength(120);
        b.Property(l => l.City).HasMaxLength(120);
        b.HasIndex(l => new { l.TenantId, l.Name });
    }
}
