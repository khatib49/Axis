// Models/LoyaltyTicket.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Models
{
    [Table("loyalty_tickets")]
    public class LoyaltyTicket
    {
        [Key]
        [Column("ticket_id")]
        public int TicketId { get; set; }

        [Required]
        [Column("customer_phone")]
        [MaxLength(20)]
        public string CustomerPhone { get; set; } = string.Empty;

        [Required]
        [Column("transaction_id")]
        public int TransactionId { get; set; }

        [Required]
        [Column("tickets_earned")]
        public int TicketsEarned { get; set; }

        [Required]
        [Column("earned_date")]
        public DateTime EarnedDate { get; set; }

        [Required]
        [Column("draw_month")]
        [MaxLength(7)] // Format: YYYY-MM
        public string DrawMonth { get; set; } = string.Empty;

        [Required]
        [Column("is_valid")]
        public bool IsValid { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("CustomerPhone")]
        public virtual LoyaltyCustomer? Customer { get; set; }
    }
}