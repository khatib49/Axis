using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.StockOps
{
    public class LocationStockConfiguration : IEntityTypeConfiguration<LocationStock>
    {
        public void Configure(EntityTypeBuilder<LocationStock> e)
        {
            e.ToTable("StockOps_LocationStocks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LocationId, x.IngredientId }).IsUnique();
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
