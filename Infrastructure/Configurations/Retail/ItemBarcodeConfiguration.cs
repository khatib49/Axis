using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Retail
{
    public class ItemBarcodeConfiguration : IEntityTypeConfiguration<ItemBarcode>
    {
        public void Configure(EntityTypeBuilder<ItemBarcode> e)
        {
            e.ToTable("Retail_Barcodes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.ItemId, x.VariantId });
            // Service also enforces it; this guards against races.
            e.HasIndex(x => new { x.ItemId, x.VariantId, x.IsPrimary })
                .IsUnique()
                .HasFilter("\"IsPrimary\" = true");
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ItemVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
