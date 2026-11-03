using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class BillSplitConfiguration : IEntityTypeConfiguration<BillSplit>
    {
        public void Configure(EntityTypeBuilder<BillSplit> e)
        {
            e.ToTable("Floor_BillSplits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            e.Property(x => x.Method).HasMaxLength(20);
            e.Property(x => x.PaidBy).HasMaxLength(256);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => x.TransactionId);
            e.HasOne<TransactionRecord>()
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
