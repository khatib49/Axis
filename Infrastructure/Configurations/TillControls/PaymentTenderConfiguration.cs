using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class PaymentTenderConfiguration : IEntityTypeConfiguration<PaymentTender>
    {
        public void Configure(EntityTypeBuilder<PaymentTender> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            b.Property(x => x.Method).HasMaxLength(20).IsRequired();
            b.Property(x => x.Reference).HasMaxLength(200);
            b.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.TransactionId);
            b.HasIndex(x => x.ShiftId);
            b.HasIndex(x => x.BranchId);

            b.HasOne<TransactionRecord>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<CashierShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<Currency>().WithMany().HasForeignKey(x => x.CurrencyCode).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
