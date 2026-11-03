using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
    {
        public void Configure(EntityTypeBuilder<ExchangeRate> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
            b.HasIndex(x => new { x.CurrencyCode, x.EffectiveFrom });
            b.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyCode).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
