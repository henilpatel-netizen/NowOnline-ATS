using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

// TenantSettings was previously conventional. Only the branding columns are configured here, so
// nothing about the existing columns changes.
public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> b)
    {
        b.Property(s => s.BrandAccentColor).HasMaxLength(9);
        b.Property(s => s.CareerHeroHeadline).HasMaxLength(160);
        b.Property(s => s.CareerHeroHeadlineOutlined).HasMaxLength(160);
        b.Property(s => s.CareerHeroIntro).HasMaxLength(600);
    }
}
