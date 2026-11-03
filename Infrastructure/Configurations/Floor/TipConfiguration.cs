using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class TipConfiguration : IEntityTypeConfiguration<Tip>
    {
        public void Configure(EntityTypeBuilder<Tip> e)
        {
            e.ToTable("Floor_Tips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            e.Property(x => x.Method).HasMaxLength(20);
            e.Property(x => x.StaffName).HasMaxLength(256);
            e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(256);
            e.HasIndex(x => x.TransactionId);
            e.HasIndex(x => x.CreatedOn);
            e.HasOne<TransactionRecord>()
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
