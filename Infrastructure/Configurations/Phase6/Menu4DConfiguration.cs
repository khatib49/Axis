using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Phase6
{
    public class Item4DProfileConfiguration : IEntityTypeConfiguration<Item4DProfile>
    {
        public void Configure(EntityTypeBuilder<Item4DProfile> b)
        {
            b.ToTable("Menu4D_Profiles");
            b.HasIndex(x => x.ItemId).IsUnique();
            b.HasIndex(x => x.GenStatus);
            b.Property(x => x.RealSizeCm).HasPrecision(6, 1);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
