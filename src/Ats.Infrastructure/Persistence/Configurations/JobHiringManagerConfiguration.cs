using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class JobHiringManagerConfiguration : IEntityTypeConfiguration<JobHiringManager>
{
    public void Configure(EntityTypeBuilder<JobHiringManager> b)
    {
        b.ToTable("JobHiringManagers");
        b.HasKey(h => h.Id);
        b.HasIndex(h => new { h.TenantId, h.JobId, h.UserId }).IsUnique();
        // Scope lookups: which jobs is this hiring manager on.
        b.HasIndex(h => new { h.TenantId, h.UserId });
        b.HasOne<Job>().WithMany(j => j.HiringManagers).HasForeignKey(h => h.JobId).OnDelete(DeleteBehavior.Cascade);
        // Restrict: users are deactivated, never deleted.
        b.HasOne<AppUser>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
