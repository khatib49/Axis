using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Offline
{
    public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
    {
        public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Key).IsUnique();
            b.Property(x => x.Key).HasMaxLength(64).IsRequired();
            b.Property(x => x.Endpoint).HasMaxLength(64).IsRequired();
            b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.ResponseBody).HasColumnType("text").IsRequired();
            b.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.CreatedOn);
        }
    }
}
