using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.StockOps
{
    public class ReorderRuleConfiguration : IEntityTypeConfiguration<ReorderRule>
    {
        public void Configure(EntityTypeBuilder<ReorderRule> e)
        {
            e.ToTable("StockOps_ReorderRules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IngredientId).IsUnique();
            e.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.PreferredSupplierId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
