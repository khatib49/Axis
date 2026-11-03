using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.OrderingChannels
{
    public class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
    {
        public void Configure(EntityTypeBuilder<DeliveryZone> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Name);
            b.HasIndex(x => x.BranchId);
        }
    }
}
