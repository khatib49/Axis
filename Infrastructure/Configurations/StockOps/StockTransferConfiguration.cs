using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.StockOps
{
    public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
    {
        public void Configure(EntityTypeBuilder<StockTransfer> e)
        {
            e.ToTable("StockOps_StockTransfers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.Transfer).HasForeignKey(l => l.TransferId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
    {
        public void Configure(EntityTypeBuilder<StockTransferLine> e)
        {
            e.ToTable("StockOps_StockTransferLines");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TransferId);
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
