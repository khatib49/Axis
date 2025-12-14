using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Models
{
    [Table("loyalty_customers")]
    public class LoyaltyCustomer
    {
        [Key]
        [Column("phone_number")]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Column("name")]
        [MaxLength(100)]
        public string? Name { get; set; }

        [Column("total_tickets_current_month")]
        public int TotalTicketsCurrentMonth { get; set; } = 0;

        [Column("last_updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<LoyaltyTicket> Tickets { get; set; } = new List<LoyaltyTicket>();
        public virtual ICollection<WeeklyWinner> WeeklyWins { get; set; } = new List<WeeklyWinner>();
        public virtual ICollection<MonthlyWinner> MonthlyWins { get; set; } = new List<MonthlyWinner>();
    }
}