using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Promotions
{
    public class GiftCardConfiguration : IEntityTypeConfiguration<GiftCard>
    {
        public void Configure(EntityTypeBuilder<GiftCard> e)
        {
            e.ToTable("Promo_GiftCards");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Transactions)
             .WithOne(t => t.GiftCard)
             .HasForeignKey(t => t.GiftCardId)
             .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class GiftCardTransactionConfiguration : IEntityTypeConfiguration<GiftCardTransaction>
    {
        public void Configure(EntityTypeBuilder<GiftCardTransaction> e)
        {
            e.ToTable("Promo_GiftCardTransactions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TransactionId);
            // Reference the existing invoice by id; SetNull keeps the card
            // ledger intact if an invoice is ever deleted.
            e.HasOne<TransactionRecord>()
             .WithMany()
             .HasForeignKey(x => x.TransactionId)
             .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
