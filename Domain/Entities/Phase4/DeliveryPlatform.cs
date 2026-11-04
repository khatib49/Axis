using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A delivery app connected to Axis (e.g. Toters). Orders arrive through the
    /// existing webhook (api/delivery/webhook/{Source}); this row adds the menu
    /// feed, item mapping and status updates sent back to the app.
    /// </summary>
    [Table("Delivery_Platforms")]
    public class DeliveryPlatform
    {
        [Key] public int Id { get; set; }
        /// <summary>Lower-case slug, same as the webhook {source} and the DeliveryOrder.ExternalSource.</summary>
        [Required][MaxLength(60)] public string Source { get; set; } = default!;
        [Required][MaxLength(120)] public string DisplayName { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        /// <summary>The app's endpoint that receives order status updates. Empty = no status push.</summary>
        [MaxLength(1000)] public string? StatusCallbackUrl { get; set; }
        /// <summary>Percentage added to menu prices in the feed (app commission), e.g. 15.</summary>
        [Column(TypeName = "numeric(9,2)")] public decimal PriceMarkupPercent { get; set; }
        /// <summary>Move new app orders straight to Preparing (accepted) instead of Pending.</summary>
        public bool AutoAccept { get; set; }
        /// <summary>Only items that are mapped appear in the menu feed.</summary>
        public bool MappedItemsOnly { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
    }

    /// <summary>App item id ↔ Axis item.</summary>
    [Table("Delivery_ItemMappings")]
    public class DeliveryItemMapping
    {
        [Key] public int Id { get; set; }
        public int PlatformId { get; set; }
        [Required][MaxLength(120)] public string ExternalItemId { get; set; } = default!;
        public int ItemId { get; set; }
        /// <summary>Price override in the feed (before markup is ignored when set).</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal? PriceOverride { get; set; }
        public bool IsAvailable { get; set; } = true;
    }

    /// <summary>One status update to send to the app; retried until delivered.</summary>
    [Table("Delivery_PlatformPushes")]
    public class DeliveryPlatformPush
    {
        [Key] public int Id { get; set; }
        public int PlatformId { get; set; }
        public int DeliveryOrderId { get; set; }
        /// <summary>The DeliveryStatusEvent that caused this push (unique: one push per event).</summary>
        public int StatusEventId { get; set; }
        [Required][MaxLength(120)] public string ExternalRef { get; set; } = default!;
        [Required][MaxLength(30)] public string Status { get; set; } = default!;
        /// <summary>Pending | Sent | Failed</summary>
        [Required][MaxLength(10)] public string State { get; set; } = DeliveryPushStates.Pending;
        public int Attempts { get; set; }
        public int? LastHttpStatus { get; set; }
        [MaxLength(1000)] public string? LastError { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? SentOn { get; set; }
        public DateTime? NextAttemptOn { get; set; }
    }

    public static class DeliveryPushStates
    {
        public const string Pending = "Pending";
        public const string Sent = "Sent";
        public const string Failed = "Failed";
        public const int MaxAttempts = 6;
    }
}
