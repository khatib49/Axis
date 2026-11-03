using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.StockOps
{
    public class StockLocationConfiguration : IEntityTypeConfiguration<StockLocation>
    {
        public void Configure(EntityTypeBuilder<StockLocation> e)
        {
            e.ToTable("StockOps_Locations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.IsDefault);
        }
    }
}
