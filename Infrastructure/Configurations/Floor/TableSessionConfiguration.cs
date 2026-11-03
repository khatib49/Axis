using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class TableSessionConfiguration : IEntityTypeConfiguration<TableSession>
    {
        public void Configure(EntityTypeBuilder<TableSession> e)
        {
            e.ToTable("Floor_TableSessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.OpenedBy).IsRequired().HasMaxLength(256);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TableId, x.Status });
            e.HasIndex(x => x.TransactionId);
            e.HasOne(x => x.Table)
                .WithMany()
                .HasForeignKey(x => x.TableId)
                .OnDelete(DeleteBehavior.Restrict);
            // FK to the existing transactions table, configured from the new side only.
            e.HasOne<TransactionRecord>()
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
