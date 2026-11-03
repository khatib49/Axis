using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.AccountingPlus
{
    public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
    {
        public void Configure(EntityTypeBuilder<BankAccount> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Name);
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.GlAccountId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class BankStatementConfiguration : IEntityTypeConfiguration<BankStatement>
    {
        public void Configure(EntityTypeBuilder<BankStatement> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.BankAccountId, x.PeriodTo });
            b.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StatementId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
    {
        public void Configure(EntityTypeBuilder<BankStatementLine> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.StatementId, x.MatchStatus });
            b.HasIndex(x => x.MatchedJournalEntryLineId);
            b.HasOne<JournalEntryLine>().WithMany().HasForeignKey(x => x.MatchedJournalEntryLineId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
