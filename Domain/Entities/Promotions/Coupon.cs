using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A voucher / coupon code. Percent or fixed discount with optional
    /// min-order, cap, usage limits, validity window and category restriction.
    /// </summary>
    [Table("Promo_Coupons")]
    public class Coupon
    {
        public int Id { get; set; }

        /// <summary>Stored uppercase, unique.</summary>
        [Required][MaxLength(40)] public string Code { get; set; } = default!;
        [Required][MaxLength(150)] public string Name { get; set; } = default!;

        /// <summary>Percent | Fixed</summary>
        [Required][MaxLength(10)] public string Type { get; set; } = CouponType.Percent;

        /// <summary>Percent (0-100) or fixed money amount depending on Type.</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal Value { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal? MinOrderAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? MaxDiscountAmount { get; set; }

        public int? MaxUses { get; set; }
        public int? MaxUsesPerCustomer { get; set; }
        public int UsedCount { get; set; }

        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }

        /// <summary>Comma separated Category ids the coupon applies to; null/empty = whole order.</summary>
        [MaxLength(500)] public string? CategoryIdsCsv { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(200)] public string CreatedBy { get; set; } = "system";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public int? BranchId { get; set; }

        public ICollection<CouponRedemption> Redemptions { get; set; } = new List<CouponRedemption>();
    }

    public static class CouponType
    {
        public const string Percent = "Percent";
        public const string Fixed = "Fixed";
    }

    /// <summary>One use of a coupon on one invoice. Unique (CouponId, TransactionId).</summary>
    [Table("Promo_CouponRedemptions")]
    public class CouponRedemption
    {
        public int Id { get; set; }

        public int CouponId { get; set; }
        public Coupon Coupon { get; set; } = default!;

        /// <summary>The existing invoice (transactions.Id).</summary>
        public int TransactionId { get; set; }

        public int? UserId { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal DiscountAmount { get; set; }

        public DateTime RedeemedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string RedeemedBy { get; set; } = "system";
    }
}
