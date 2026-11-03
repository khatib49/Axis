using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Promotions
{
    public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
    {
        public void Configure(EntityTypeBuilder<Coupon> e)
        {
            e.ToTable("Promo_Coupons");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.IsActive);
            e.HasMany(x => x.Redemptions)
             .WithOne(r => r.Coupon)
             .HasForeignKey(r => r.CouponId)
             .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
    {
        public void Configure(EntityTypeBuilder<CouponRedemption> e)
        {
            e.ToTable("Promo_CouponRedemptions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CouponId, x.TransactionId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<TransactionRecord>()
             .WithMany()
             .HasForeignKey(x => x.TransactionId)
             .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
