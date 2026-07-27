using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ats.Infrastructure.Persistence.Configurations;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.ResponseBody).HasMaxLength(2000);
        b.HasIndex(d => new { d.TenantId, d.OutboxMessageId });
        b.HasOne<OutboxMessage>().WithMany().HasForeignKey(d => d.OutboxMessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
