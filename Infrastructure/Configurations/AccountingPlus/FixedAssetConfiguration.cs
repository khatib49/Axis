using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.AccountingPlus
{
    public class FixedAssetConfiguration : IEntityTypeConfiguration<FixedAsset>
    {
        public void Configure(EntityTypeBuilder<FixedAsset> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Status);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AssetGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccumulatedDepGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.DepExpenseGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.DisposalJournalEntryId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.DepreciationEntries).WithOne().HasForeignKey(d => d.AssetId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class DepreciationEntryConfiguration : IEntityTypeConfiguration<DepreciationEntry>
    {
        public void Configure(EntityTypeBuilder<DepreciationEntry> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.AssetId, x.PeriodYear, x.PeriodMonth }).IsUnique();
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.SetNull);
        }
    }

    public class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
    {
        public void Configure(EntityTypeBuilder<AccountingPeriod> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.Year, x.Month }).IsUnique();
        }
    }
}
