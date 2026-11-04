using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Phase4
{
    public class ItemModel3DConfiguration : IEntityTypeConfiguration<ItemModel3D>
    {
        public void Configure(EntityTypeBuilder<ItemModel3D> b)
        {
            b.HasIndex(x => x.ItemId).IsUnique();
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class UpsellRuleConfiguration : IEntityTypeConfiguration<UpsellRule>
    {
        public void Configure(EntityTypeBuilder<UpsellRule> b)
        {
            b.HasIndex(x => x.TriggerItemId);
            b.HasIndex(x => x.TriggerCategoryId);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.SuggestItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.TriggerItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Category>().WithMany().HasForeignKey(x => x.TriggerCategoryId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class UpsellPairConfiguration : IEntityTypeConfiguration<UpsellPair>
    {
        public void Configure(EntityTypeBuilder<UpsellPair> b)
        {
            b.HasIndex(x => new { x.ItemId, x.SuggestItemId }).IsUnique();
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.SuggestItemId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class UpsellEventConfiguration : IEntityTypeConfiguration<UpsellEvent>
    {
        public void Configure(EntityTypeBuilder<UpsellEvent> b)
        {
            b.HasIndex(x => x.CreatedOn);
            b.HasIndex(x => x.SuggestItemId);
        }
    }

    public class DeliveryPlatformConfiguration : IEntityTypeConfiguration<DeliveryPlatform>
    {
        public void Configure(EntityTypeBuilder<DeliveryPlatform> b)
        {
            b.HasIndex(x => x.Source).IsUnique();
        }
    }

    public class DeliveryItemMappingConfiguration : IEntityTypeConfiguration<DeliveryItemMapping>
    {
        public void Configure(EntityTypeBuilder<DeliveryItemMapping> b)
        {
            b.HasIndex(x => new { x.PlatformId, x.ExternalItemId }).IsUnique();
            b.HasIndex(x => new { x.PlatformId, x.ItemId });
            b.HasOne<DeliveryPlatform>().WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class DeliveryPlatformPushConfiguration : IEntityTypeConfiguration<DeliveryPlatformPush>
    {
        public void Configure(EntityTypeBuilder<DeliveryPlatformPush> b)
        {
            b.HasIndex(x => x.StatusEventId).IsUnique();
            b.HasIndex(x => new { x.State, x.NextAttemptOn });
            b.HasOne<DeliveryPlatform>().WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<DeliveryOrder>().WithMany().HasForeignKey(x => x.DeliveryOrderId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class CardTerminalConfiguration : IEntityTypeConfiguration<CardTerminal>
    {
        public void Configure(EntityTypeBuilder<CardTerminal> b)
        {
            b.HasIndex(x => x.BranchId);
        }
    }

    public class TerminalPaymentConfiguration : IEntityTypeConfiguration<TerminalPayment>
    {
        public void Configure(EntityTypeBuilder<TerminalPayment> b)
        {
            b.HasIndex(x => x.ExternalId).IsUnique();
            b.HasIndex(x => x.TransactionId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.CreatedOn);
            b.HasOne<TransactionRecord>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<CardTerminal>().WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
