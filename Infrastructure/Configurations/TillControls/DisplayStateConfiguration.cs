using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class DisplayStateConfiguration : IEntityTypeConfiguration<DisplayState>
    {
        public void Configure(EntityTypeBuilder<DisplayState> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.RegisterName).IsUnique();
            b.HasIndex(x => x.Token).IsUnique();
            b.Property(x => x.CartJson).HasColumnType("text");
            b.HasOne<TransactionRecord>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<WhishRequest>().WithMany().HasForeignKey(x => x.WhishRequestId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
