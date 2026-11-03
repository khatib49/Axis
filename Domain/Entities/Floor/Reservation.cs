using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A booking for a table, a gaming room or a console (game) in a time window.</summary>
    [Table("Floor_Reservations")]
    public class Reservation
    {
        public const string StatusPending = "Pending";
        public const string StatusConfirmed = "Confirmed";
        public const string StatusSeated = "Seated";
        public const string StatusNoShow = "NoShow";
        public const string StatusCancelled = "Cancelled";
        public const string StatusCompleted = "Completed";

        public const string ResourceTable = "Table";
        public const string ResourceRoom = "Room";
        public const string ResourceConsole = "Console";

        public int Id { get; set; }
        [Required][MaxLength(120)] public string CustomerName { get; set; } = default!;
        [Required][MaxLength(40)] public string Phone { get; set; } = default!;
        /// <summary>Existing AppUser id (no navigation).</summary>
        public int? UserId { get; set; }
        /// <summary>Table | Room | Console.</summary>
        [MaxLength(10)] public string ResourceType { get; set; } = ResourceTable;
        public int? TableId { get; set; }
        /// <summary>Existing Room id (no navigation).</summary>
        public int? RoomId { get; set; }
        /// <summary>Existing Game id (no navigation).</summary>
        public int? GameId { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public int PartySize { get; set; } = 1;
        /// <summary>Pending | Confirmed | Seated | NoShow | Cancelled | Completed.</summary>
        [MaxLength(12)] public string Status { get; set; } = StatusPending;
        [MaxLength(500)] public string? Notes { get; set; }
        /// <summary>Phone | WalkIn | Online.</summary>
        [MaxLength(10)] public string Source { get; set; } = "Phone";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = default!;
        public DateTime? ConfirmedOn { get; set; }
        /// <summary>TransactionRecord opened when the party was seated (no navigation).</summary>
        public int? SeatedTransactionId { get; set; }
        public int? BranchId { get; set; }
    }
}
