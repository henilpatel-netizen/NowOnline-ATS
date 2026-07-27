using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class ApplicationEventConfiguration : IEntityTypeConfiguration<ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ApplicationEvent> b)
    {
        b.HasKey(e => e.Id);
        b.HasIndex(e => new { e.TenantId, e.ApplicationId, e.OccurredAt });
        b.HasOne<JobApplication>().WithMany().HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}
