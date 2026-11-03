using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
    {
        public void Configure(EntityTypeBuilder<DiningTable> e)
        {
            e.ToTable("Floor_Tables");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(20);
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.Property(x => x.Shape).HasMaxLength(10);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Area)
                .WithMany(a => a.Tables)
                .HasForeignKey(x => x.AreaId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
