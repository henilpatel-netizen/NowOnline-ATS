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
        // Hot paths: status filters (dashboard/shell/list) and candidate-id lookups
        // (CandidateListQuery's ids.Contains) — the unique index above can't serve either.
        b.HasIndex(a => new { a.TenantId, a.Status });
        b.HasIndex(a => new { a.TenantId, a.CandidateId });
        b.HasOne(a => a.Candidate).WithMany().HasForeignKey(a => a.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Job>().WithMany().HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PipelineStage>().WithMany().HasForeignKey(a => a.CurrentStageId).OnDelete(DeleteBehavior.Restrict);
    }
}
