using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Promotions
{
    public class HappyHourRuleConfiguration : IEntityTypeConfiguration<HappyHourRule>
    {
        public void Configure(EntityTypeBuilder<HappyHourRule> e)
        {
            e.ToTable("Promo_HappyHourRules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IsActive);
        }
    }

    public class HappyHourApplicationConfiguration : IEntityTypeConfiguration<HappyHourApplication>
    {
        public void Configure(EntityTypeBuilder<HappyHourApplication> e)
        {
            e.ToTable("Promo_HappyHourApplications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TransactionId).IsUnique();
            e.HasOne(x => x.Rule)
             .WithMany()
             .HasForeignKey(x => x.RuleId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TransactionRecord>()
             .WithMany()
             .HasForeignKey(x => x.TransactionId)
             .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
