using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Identity;

namespace Domain.Entities
{
    /// <summary>
    /// An order placed on the public website by a signed-in customer.
    ///
    /// Life cycle
    ///   PayAtPickup : New → Accepted (open invoice created at the till, kitchen
    ///                 ticket printed) → Ready → Completed (invoice closed at
    ///                 pickup) | Cancelled
    ///   Online      : AwaitingPayment → (MontyPay settled) Paid → Accepted →
    ///                 Ready → Completed | Cancelled
    ///
    /// Prices are snapshotted at order time; the linked TransactionRecord is
    /// the accounting truth once the till accepts the order.
    /// </summary>
    [Table("OnlineOrders")]
    public class OnlineOrder
    {
        [Key] public int Id { get; set; }

        /// <summary>Short public reference shown to the customer, e.g. "AX-4F7K2Q".</summary>
        [Required][MaxLength(16)] public string Code { get; set; } = default!;

        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        [Required][MaxLength(150)] public string CustomerName { get; set; } = default!;
        [Required][MaxLength(40)] public string CustomerPhone { get; set; } = default!;
        [MaxLength(200)] public string? CustomerEmail { get; set; }

        /// <summary>"Pickup" | "Delivery"</summary>
        [Required][MaxLength(20)] public string Fulfilment { get; set; } = "Pickup";
        /// <summary>"PayAtPickup" | "Online" | "COD" (cash on delivery, collected by the courier)</summary>
        [Required][MaxLength(20)] public string PaymentMode { get; set; } = "PayAtPickup";
        /// <summary>
        /// New | AwaitingPayment | Paid | Accepted | Ready | Shipped | Delivered | Completed | Cancelled
        /// (Ready is the pickup path; Shipped/Delivered the delivery path.)
        /// </summary>
        [Required][MaxLength(20)] public string Status { get; set; } = "New";

        [Column(TypeName = "numeric(18,2)")] public decimal Subtotal { get; set; }
        /// <summary>Shipping charged to the customer (0 for pickup / free delivery).</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal DeliveryFee { get; set; }
        /// <summary>Subtotal + DeliveryFee.</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal Total { get; set; }

        // ── Delivery ─────────────────────────────────────────────────────
        /// <summary>zone | aramex | free | manual — how DeliveryFee was decided.</summary>
        [MaxLength(20)] public string? RateSource { get; set; }
        public int? ShippingZoneId { get; set; }
        [MaxLength(200)] public string? AddressLine1 { get; set; }
        [MaxLength(200)] public string? AddressLine2 { get; set; }
        [MaxLength(100)] public string? City { get; set; }
        [MaxLength(100)] public string? Region { get; set; }
        [MaxLength(2)] public string CountryCode { get; set; } = "LB";
        [MaxLength(500)] public string? DeliveryNotes { get; set; }
        /// <summary>Total parcel weight computed at order time (kg).</summary>
        [Column(TypeName = "numeric(9,3)")] public decimal? WeightKg { get; set; }
        public DateTime? ShippedOn { get; set; }
        public DateTime? DeliveredOn { get; set; }
        /// <summary>Journal entry that booked the delivery fee to 4400 (null until delivered/paid).</summary>
        public int? FeeJournalEntryId { get; set; }

        public bool IsDelivery => string.Equals(Fulfilment, "Delivery", StringComparison.OrdinalIgnoreCase);
        [MaxLength(500)] public string? Notes { get; set; }
        /// <summary>When the customer wants to collect (free text like "18:30" / "ASAP").</summary>
        [MaxLength(60)] public string? PickupTime { get; set; }

        /// <summary>Till invoice created on accept (pay at pickup) or on payment (online).</summary>
        public int? TransactionRecordId { get; set; }
        /// <summary>MontyPay payment when PaymentMode = Online.</summary>
        public int? OnlinePaymentId { get; set; }

        [MaxLength(200)] public string? HandledBy { get; set; }
        [MaxLength(500)] public string? CancelReason { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? PaidOn { get; set; }
        public DateTime? AcceptedOn { get; set; }
        public DateTime? ReadyOn { get; set; }
        public DateTime? CompletedOn { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public ICollection<OnlineOrderLine> Lines { get; set; } = new List<OnlineOrderLine>();
    }

    /// <summary>One item (and one colour/type option) on a website order. Snapshotted.</summary>
    [Table("OnlineOrderLines")]
    public class OnlineOrderLine
    {
        [Key] public int Id { get; set; }
        public int OnlineOrderId { get; set; }
        public OnlineOrder Order { get; set; } = default!;

        public int ItemId { get; set; }
        [Required][MaxLength(200)] public string ItemName { get; set; } = default!;
        [Column(TypeName = "numeric(18,2)")] public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }

        public int? VariantId { get; set; }
        [MaxLength(80)] public string? VariantName { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal VariantPriceDelta { get; set; }

        /// <summary>JSON: [{"addOnId":1,"name":"Oat Milk","quantity":1,"unitPrice":1.0}]</summary>
        public string? AddOnsJson { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal AddOnsTotal { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal LineTotal { get; set; }
    }
}
