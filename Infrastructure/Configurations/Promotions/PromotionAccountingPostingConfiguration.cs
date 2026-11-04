using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Promotions
{
    public class PromotionAccountingPostingConfiguration : IEntityTypeConfiguration<PromotionAccountingPosting>
    {
        public void Configure(EntityTypeBuilder<PromotionAccountingPosting> e)
        {
            e.ToTable("Promo_AccountingPostings");
            e.HasKey(x => x.Id);
            // Once-only guarantee: one posting per (reference, sequence).
            e.HasIndex(x => new { x.ReferenceType, x.ReferenceId, x.Sequence }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.On);
            e.HasIndex(x => x.InvoiceId);
            // The journal entry is referenced by id; SetNull keeps the posting
            // history if an entry is ever deleted (the row then shows as Pending
            // again on the next repost only if its status is reset by an admin).
            e.HasOne<JournalEntry>()
             .WithMany()
             .HasForeignKey(x => x.JournalEntryId)
             .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
