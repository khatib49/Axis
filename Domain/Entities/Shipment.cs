using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A flat delivery-fee zone for website orders: a list of city names and
    /// the fee charged. "FreeAbove" makes delivery free when the order's
    /// subtotal reaches that amount.
    /// </summary>
    [Table("ShippingZones")]
    public class ShippingZone
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(100)] public string Name { get; set; } = default!;
        /// <summary>Comma-separated city names, matched case-insensitively.</summary>
        public string Cities { get; set; } = "";
        [Column(TypeName = "numeric(18,2)")] public decimal Fee { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? FreeAbove { get; set; }
        [MaxLength(40)] public string? EstimatedDays { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public IEnumerable<string> CityList() =>
            (Cities ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// One courier shipment (Aramex AWB) for a website order. The raw
    /// request/response are kept for support; tracking updates are appended
    /// to <see cref="Events"/> by the Hangfire poller.
    ///
    /// Status: Created → PickedUp → InTransit → OutForDelivery → Delivered
    ///         | Returned | Failed | Cancelled
    /// </summary>
    [Table("Shipments")]
    public class Shipment
    {
        [Key] public int Id { get; set; }
        public int OnlineOrderId { get; set; }
        public OnlineOrder Order { get; set; } = default!;

        [Required][MaxLength(20)] public string Provider { get; set; } = "Aramex";
        [Required][MaxLength(20)] public string Environment { get; set; } = "sandbox";
        [MaxLength(40)] public string? AwbNumber { get; set; }
        [MaxLength(40)] public string? ForeignHawb { get; set; }
        [Required][MaxLength(20)] public string Status { get; set; } = "Created";

        [MaxLength(5)] public string? ProductGroup { get; set; }
        [MaxLength(5)] public string? ProductType { get; set; }
        [MaxLength(2)] public string? PaymentType { get; set; }
        [MaxLength(50)] public string? Services { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal CodAmount { get; set; }
        [MaxLength(3)] public string? CodCurrency { get; set; }
        [Column(TypeName = "numeric(9,3)")] public decimal WeightKg { get; set; } = 0.5m;
        public int Pieces { get; set; } = 1;

        [MaxLength(500)] public string? LabelUrl { get; set; }
        [MaxLength(64)] public string? PickupGuid { get; set; }
        [MaxLength(40)] public string? PickupId { get; set; }

        [MaxLength(20)] public string? LastTrackingCode { get; set; }
        [MaxLength(300)] public string? LastTrackingText { get; set; }
        public DateTime? LastTrackingAt { get; set; }
        public DateTime? LastPolledAt { get; set; }

        public string? RequestJson { get; set; }
        public string? ResponseJson { get; set; }
        [MaxLength(1000)] public string? Error { get; set; }

        [MaxLength(200)] public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? DeliveredOn { get; set; }
        /// <summary>When Aramex remitted the COD cash (settlement booked).</summary>
        public DateTime? SettledOn { get; set; }
        public int? SettlementJournalEntryId { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public ICollection<ShipmentEvent> Events { get; set; } = new List<ShipmentEvent>();

        public bool IsOpen => Status is "Created" or "PickedUp" or "InTransit" or "OutForDelivery";
    }

    /// <summary>A tracking update from the courier (deduped on ShipmentId + Code + EventAt).</summary>
    [Table("ShipmentEvents")]
    public class ShipmentEvent
    {
        [Key] public int Id { get; set; }
        public int ShipmentId { get; set; }
        public Shipment Shipment { get; set; } = default!;
        [MaxLength(20)] public string? Code { get; set; }
        [MaxLength(300)] public string? Description { get; set; }
        [MaxLength(200)] public string? Location { get; set; }
        [MaxLength(500)] public string? Comments { get; set; }
        public DateTime EventAt { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
