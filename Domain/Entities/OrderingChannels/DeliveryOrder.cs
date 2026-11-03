using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Delivery / takeaway wrapper around an existing open F&B invoice
    /// (TransactionRecord created through CreateCoffeeShopOrder).
    /// </summary>
    [Table("Delivery_Orders")]
    public class DeliveryOrder
    {
        [Key] public int Id { get; set; }
        public int TransactionId { get; set; }
        /// <summary>Delivery | Takeaway</summary>
        [Required][MaxLength(20)] public string Type { get; set; } = DeliveryOrderTypes.Delivery;
        public int? ZoneId { get; set; }
        public int? DriverId { get; set; }
        [Required][MaxLength(150)] public string CustomerName { get; set; } = default!;
        [MaxLength(40)] public string? Phone { get; set; }
        [MaxLength(500)] public string? Address { get; set; }
        [MaxLength(1000)] public string? Notes { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal DeliveryFee { get; set; }
        /// <summary>Pending | Preparing | Ready | OutForDelivery | Delivered | Cancelled</summary>
        [Required][MaxLength(30)] public string Status { get; set; } = DeliveryStatuses.Pending;
        public DateTime? ScheduledFor { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(200)] public string CreatedBy { get; set; } = "system";
        public DateTime? AssignedOn { get; set; }
        public DateTime? PickedUpOn { get; set; }
        public DateTime? DeliveredOn { get; set; }
        [MaxLength(500)] public string? CancelledReason { get; set; }
        /// <summary>Aggregator / kiosk / selforder source name (e.g. "toters"). Null = created at the till.</summary>
        [MaxLength(60)] public string? ExternalSource { get; set; }
        /// <summary>Aggregator order reference; unique per ExternalSource (idempotency key).</summary>
        [MaxLength(120)] public string? ExternalRef { get; set; }
        public int? BranchId { get; set; }

        public DeliveryZone? Zone { get; set; }
        public Driver? Driver { get; set; }
        public ICollection<DeliveryStatusEvent> StatusEvents { get; set; } = new List<DeliveryStatusEvent>();
    }

    public static class DeliveryOrderTypes
    {
        public const string Delivery = "Delivery";
        public const string Takeaway = "Takeaway";
        public static readonly string[] All = { Delivery, Takeaway };
    }

    public static class DeliveryStatuses
    {
        public const string Pending = "Pending";
        public const string Preparing = "Preparing";
        public const string Ready = "Ready";
        public const string OutForDelivery = "OutForDelivery";
        public const string Delivered = "Delivered";
        public const string Cancelled = "Cancelled";
        public static readonly string[] All = { Pending, Preparing, Ready, OutForDelivery, Delivered, Cancelled };
        public static bool IsTerminal(string s) => s == Delivered || s == Cancelled;
    }
}
