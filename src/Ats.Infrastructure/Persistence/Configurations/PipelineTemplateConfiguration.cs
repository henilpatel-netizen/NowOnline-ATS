using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class PipelineTemplateConfiguration : IEntityTypeConfiguration<PipelineTemplate>
{
    public void Configure(EntityTypeBuilder<PipelineTemplate> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).IsRequired().HasMaxLength(120);
        b.HasMany(p => p.Stages).WithOne().HasForeignKey(s => s.PipelineTemplateId);
    }
}
