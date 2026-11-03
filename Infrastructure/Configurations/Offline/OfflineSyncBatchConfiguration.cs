using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Offline
{
    public class OfflineSyncBatchConfiguration : IEntityTypeConfiguration<OfflineSyncBatch>
    {
        public void Configure(EntityTypeBuilder<OfflineSyncBatch> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ClientId).HasMaxLength(128).IsRequired();
            b.Property(x => x.ReceivedBy).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.ReceivedOn);
            b.HasIndex(x => x.ClientId);
        }
    }
}
