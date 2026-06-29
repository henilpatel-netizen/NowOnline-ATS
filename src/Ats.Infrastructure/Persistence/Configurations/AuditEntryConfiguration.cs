using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Action).IsRequired().HasMaxLength(80);
        b.Property(a => a.EntityType).IsRequired().HasMaxLength(80);
        b.Property(a => a.EntityRef).HasMaxLength(80);
        b.Property(a => a.Summary).IsRequired().HasMaxLength(400);
        b.Property(a => a.UserName).IsRequired().HasMaxLength(200);
        b.HasIndex(a => new { a.TenantId, a.OccurredAt });
    }
}
