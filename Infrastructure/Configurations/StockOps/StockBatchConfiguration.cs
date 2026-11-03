using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.StockOps
{
    public class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
    {
        public void Configure(EntityTypeBuilder<StockBatch> e)
        {
            e.ToTable("StockOps_StockBatches");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IngredientId);
            e.HasIndex(x => x.ExpiresOn);
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PurchaseLine>().WithMany().HasForeignKey(x => x.PurchaseLineId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
