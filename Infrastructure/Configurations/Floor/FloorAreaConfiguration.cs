using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Floor
{
    public class FloorAreaConfiguration : IEntityTypeConfiguration<FloorArea>
    {
        public void Configure(EntityTypeBuilder<FloorArea> e)
        {
            e.ToTable("Floor_Areas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.HasIndex(x => new { x.BranchId, x.SortOrder });
        }
    }
}
