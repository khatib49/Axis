using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.OrderingChannels
{
    public class DeliveryStatusEventConfiguration : IEntityTypeConfiguration<DeliveryStatusEvent>
    {
        public void Configure(EntityTypeBuilder<DeliveryStatusEvent> b)
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.DeliveryOrder)
                .WithMany(o => o.StatusEvents)
                .HasForeignKey(x => x.DeliveryOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DeliveryOrderId, x.On });
        }
    }
}
