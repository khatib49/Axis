using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.StockOps
{
    public class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
    {
        public void Configure(EntityTypeBuilder<StockCount> e)
        {
            e.ToTable("StockOps_StockCounts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.LocationId);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.StockCount).HasForeignKey(l => l.StockCountId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
    {
        public void Configure(EntityTypeBuilder<StockCountLine> e)
        {
            e.ToTable("StockOps_StockCountLines");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.StockCountId, x.IngredientId }).IsUnique();
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
