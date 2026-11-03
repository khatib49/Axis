using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.OrderingChannels
{
    public class SelfOrderSessionConfiguration : IEntityTypeConfiguration<SelfOrderSession>
    {
        public void Configure(EntityTypeBuilder<SelfOrderSession> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Token).IsUnique();
            b.HasIndex(x => x.TableCode);
            b.HasIndex(x => x.TransactionId);
            b.HasOne<TransactionRecord>()
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
