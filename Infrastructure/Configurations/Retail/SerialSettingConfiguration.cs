using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Retail
{
    public class SerialSettingConfiguration : IEntityTypeConfiguration<SerialSetting>
    {
        public void Configure(EntityTypeBuilder<SerialSetting> e)
        {
            e.ToTable("Retail_SerialSettings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ItemId).IsUnique();
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
