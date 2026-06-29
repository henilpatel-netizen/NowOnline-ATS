using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.Name).IsRequired().HasMaxLength(120);
        b.HasIndex(d => new { d.TenantId, d.Name });
    }
}
