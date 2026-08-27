using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> b)
    {
        b.HasKey(c => c.Id);
        b.Ignore(c => c.FullName);
        b.Property(c => c.FirstName).IsRequired().HasMaxLength(100);
        b.Property(c => c.LastName).IsRequired().HasMaxLength(100);
        b.Property(c => c.Email).IsRequired().HasMaxLength(256);
        b.Property(c => c.Phone).HasMaxLength(40);
        b.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
        // Default candidate-list sort (LastName, FirstName).
        b.HasIndex(c => new { c.TenantId, c.LastName, c.FirstName });
    }
}
