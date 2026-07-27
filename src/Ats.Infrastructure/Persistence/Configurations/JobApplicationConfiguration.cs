using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable("Applications");
        b.HasKey(a => a.Id);
        b.Property(a => a.SourceCode).HasMaxLength(36);
        b.Property(a => a.RowVersion).IsRowVersion();
        b.HasIndex(a => new { a.TenantId, a.JobId, a.CandidateId }).IsUnique();
        b.HasOne(a => a.Candidate).WithMany().HasForeignKey(a => a.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Job>().WithMany().HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PipelineStage>().WithMany().HasForeignKey(a => a.CurrentStageId).OnDelete(DeleteBehavior.Restrict);
    }
}
