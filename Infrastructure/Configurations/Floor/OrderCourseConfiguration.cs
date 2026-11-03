using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class OrderCourseConfiguration : IEntityTypeConfiguration<OrderCourse>
    {
        public void Configure(EntityTypeBuilder<OrderCourse> e)
        {
            e.ToTable("Floor_OrderCourses");
            e.HasKey(x => x.Id);
            e.Property(x => x.FiredBy).HasMaxLength(256);
            e.Property(x => x.Quantity).HasDefaultValue(1).HasSentinel(0);
            // One row per seat portion of an invoice line (SeatNumber null = table / unassigned).
            e.HasIndex(x => new { x.TransactionId, x.ItemId, x.SeatNumber }).IsUnique();
            e.HasOne<TransactionRecord>()
                .WithMany()
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Item>()
                .WithMany()
                .HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
