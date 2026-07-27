using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.HasKey(m => m.Id);
        b.Property(m => m.Code).IsRequired().HasMaxLength(36);
        b.Property(m => m.ExternalVacancyId).IsRequired().HasMaxLength(36);
        b.Property(m => m.ExternalCandidateId).IsRequired().HasMaxLength(36);
        b.Property(m => m.CandidateStatus).HasMaxLength(200);
        b.Property(m => m.LastError).HasMaxLength(1000);
        b.Property(m => m.RowVersion).IsRowVersion();
        b.HasIndex(m => new { m.TenantId, m.Status, m.NextAttemptAt });
        b.HasIndex(m => new { m.TenantId, m.ApplicationId, m.Id });
    }
}
