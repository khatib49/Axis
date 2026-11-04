using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Retail
{
    public class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
    {
        public void Configure(EntityTypeBuilder<SerialNumber> e)
        {
            e.ToTable("Retail_SerialNumbers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Serial).IsUnique();
            e.HasIndex(x => new { x.ItemId, x.Status });
            e.HasIndex(x => x.TransactionId);
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ItemVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TransactionRecord>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
