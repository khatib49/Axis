using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.AccountingPlus
{
    public class SupplierBillConfiguration : IEntityTypeConfiguration<SupplierBill>
    {
        public void Configure(EntityTypeBuilder<SupplierBill> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.SupplierId, x.BillNumber }).IsUnique();
            b.HasIndex(x => new { x.Status, x.DueDate });
            b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Purchase>().WithMany().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.ExpenseGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.PayableGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.BillId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class SupplierBillPaymentConfiguration : IEntityTypeConfiguration<SupplierBillPayment>
    {
        public void Configure(EntityTypeBuilder<SupplierBillPayment> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.PaidOn);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.CashGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.SetNull);
        }
    }

    public class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
    {
        public void Configure(EntityTypeBuilder<TaxRate> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Name).IsUnique();
        }
    }
}
