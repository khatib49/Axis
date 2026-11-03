using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Promotions
{
    public class ComboConfiguration : IEntityTypeConfiguration<Combo>
    {
        public void Configure(EntityTypeBuilder<Combo> e)
        {
            e.ToTable("Promo_Combos");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsActive);
            e.HasMany(x => x.Items)
             .WithOne(i => i.Combo)
             .HasForeignKey(i => i.ComboId)
             .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class ComboItemConfiguration : IEntityTypeConfiguration<ComboItem>
    {
        public void Configure(EntityTypeBuilder<ComboItem> e)
        {
            e.ToTable("Promo_ComboItems");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ComboId, x.ItemId }).IsUnique();
            e.HasOne<Item>()
             .WithMany()
             .HasForeignKey(x => x.ItemId)
             .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
