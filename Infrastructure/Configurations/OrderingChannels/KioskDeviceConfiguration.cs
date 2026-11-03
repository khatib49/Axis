using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.OrderingChannels
{
    public class KioskDeviceConfiguration : IEntityTypeConfiguration<KioskDevice>
    {
        public void Configure(EntityTypeBuilder<KioskDevice> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Token).IsUnique();
        }
    }
}
