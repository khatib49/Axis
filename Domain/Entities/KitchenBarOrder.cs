using Domain.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    [Table("KitchenBarOrders")]
    public class KitchenBarOrder
    {
        public int Id { get; set; }

        public int TransactionId { get; set; }
        public int TransactionItemId { get; set; } // Composite key reference
        public int ItemId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Station { get; set; } = string.Empty; // "Kitchen" or "Bar"

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Preparing, Done

        public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PreparedAt { get; set; }
        public int? PreparedBy { get; set; }
        public DateTime? PrintedAt { get; set; }

        // Order Details for Receipt
        [MaxLength(50)]
        public string? TableNumber { get; set; }

        [MaxLength(200)]
        public string? GuestName { get; set; }

        public string? ItemComment { get; set; }

        public int Quantity { get; set; }

        [Required]
        [MaxLength(200)]
        public string ItemName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ItemPrice { get; set; }

        [Required]
        [MaxLength(100)]
        public string CreatedByUsername { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public TransactionRecord Transaction { get; set; } = default!;
        public Item Item { get; set; } = default!;
        public AppUser? PreparedByUser { get; set; }
    }
}
