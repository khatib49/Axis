using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.OrderingChannels
{
    public class DeliveryOrderConfiguration : IEntityTypeConfiguration<DeliveryOrder>
    {
        public void Configure(EntityTypeBuilder<DeliveryOrder> b)
        {
            b.HasKey(x => x.Id);

            // Existing invoice — relationship configured from the new side only.
            b.HasOne<TransactionRecord>()
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Zone)
                .WithMany()
                .HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(x => x.Driver)
                .WithMany()
                .HasForeignKey(x => x.DriverId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(x => x.TransactionId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.DriverId);
            b.HasIndex(x => x.CreatedOn);
            b.HasIndex(x => x.BranchId);
            // Idempotency key for aggregator webhooks (nulls are distinct in PostgreSQL).
            b.HasIndex(x => new { x.ExternalSource, x.ExternalRef }).IsUnique();
        }
    }
}
