using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.HasKey(j => j.Id);
        b.Property(j => j.Title).IsRequired().HasMaxLength(200);
        b.Property(j => j.ExternalRef).IsRequired().HasMaxLength(36);
        b.HasIndex(j => new { j.TenantId, j.ExternalRef }).IsUnique();
        b.HasIndex(j => new { j.TenantId, j.Status });
        // SetNull, not Restrict: the service already blocks deleting a lookup a live job uses, and the
        // query filter hides soft-deleted jobs from that check, so the database clears their reference.
        b.HasOne(j => j.Department).WithMany().HasForeignKey(j => j.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(j => j.Location).WithMany().HasForeignKey(j => j.LocationId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<PipelineTemplate>().WithMany().HasForeignKey(j => j.PipelineTemplateId).OnDelete(DeleteBehavior.Restrict);
    }
}
