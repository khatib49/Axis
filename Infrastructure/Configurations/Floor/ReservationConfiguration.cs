using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
    {
        public void Configure(EntityTypeBuilder<Reservation> e)
        {
            e.ToTable("Floor_Reservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerName).IsRequired().HasMaxLength(120);
            e.Property(x => x.Phone).IsRequired().HasMaxLength(40);
            e.Property(x => x.ResourceType).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Source).HasMaxLength(10);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(256);
            e.HasIndex(x => new { x.StartOn, x.Status });
            e.HasIndex(x => x.Phone);
            e.HasOne<DiningTable>().WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<TransactionRecord>().WithMany().HasForeignKey(x => x.SeatedTransactionId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
