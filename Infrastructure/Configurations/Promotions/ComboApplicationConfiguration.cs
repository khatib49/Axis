using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Promotions
{
    public class ComboApplicationConfiguration : IEntityTypeConfiguration<ComboApplication>
    {
        public void Configure(EntityTypeBuilder<ComboApplication> e)
        {
            e.ToTable("Promo_ComboApplications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TransactionId);
            e.HasIndex(x => new { x.ComboId, x.TransactionId }).IsUnique();
            e.HasOne(x => x.Combo)
             .WithMany()
             .HasForeignKey(x => x.ComboId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TransactionRecord>()
             .WithMany()
             .HasForeignKey(x => x.TransactionId)
             .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
