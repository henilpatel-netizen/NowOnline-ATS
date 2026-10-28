using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> b)
    {
        b.HasKey(m => m.Id);
        b.Property(m => m.To).IsRequired().HasMaxLength(256);
        b.Property(m => m.Subject).IsRequired().HasMaxLength(300);
        b.Property(m => m.LastError).HasMaxLength(1000);
        // The claim lease is the fencing token: a worker whose lease was reclaimed cannot save the row.
        b.Property(m => m.NextAttemptAt).IsConcurrencyToken();
        b.HasIndex(m => new { m.Status, m.NextAttemptAt });
    }
}
