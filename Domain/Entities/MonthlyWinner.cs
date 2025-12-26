using Domain.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Models
{
    [Table("monthly_winners")]
    public class MonthlyWinner
    {
        [Key]
        [Column("winner_id")]
        public int WinnerId { get; set; }

        [Required]
        [Column("customer_phone")]
        [MaxLength(20)]
        public string CustomerPhone { get; set; } = string.Empty;

        [Required]
        [Column("prize_name")]
        [MaxLength(200)]
        public string PrizeName { get; set; } = string.Empty;

        [Required]
        [Column("draw_month")]
        [MaxLength(7)] // Format: YYYY-MM
        public string DrawMonth { get; set; } = string.Empty;

        [Required]
        [Column("draw_date")]
        public DateTime DrawDate { get; set; }

        [Column("tickets_held")]
        public int TicketsHeld { get; set; }

        [Required]
        [Column("claimed")]
        public bool Claimed { get; set; } = false;

        [Column("claimed_date")]
        public DateTime? ClaimedDate { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("CustomerPhone")]
        public virtual LoyaltyCustomer? Customer { get; set; }
    }
}