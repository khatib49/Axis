using Domain.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("transactions")]
    public class TransactionRecord
    {
        public int Id { get; set; }

       
        public int? RoomId { get; set; }
        public Room? Room { get; set; }  

        public int? GameTypeId { get; set; }
        public Category? GameType { get; set; }  

        public int? GameId { get; set; }
        public Game? Game { get; set; } 

        public int? GameSettingId { get; set; }
        public Setting? GameSetting { get; set; }

        // Required fields
        public int StatusId { get; set; }
        public Status Status { get; set; } = default!;

        // NEW: link to client user (can be null)
        public int? UserId { get; set; }
        public AppUser? User { get; set; }

        // Transaction details
        public int Hours { get; set; }
        public decimal TotalPrice { get; set; }

        // Audit fields
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public string CreatedBy { get; set; } = default!;

        public int? SetId { get; set; }
        public Set? Set { get; set; } // NEW RELATION

        public DateTime? ExpectedEndOn { get; set; } // NEW: UTC timestamp when it should end
        public string? HangfireJobId { get; set; }   // NEW: to cancel/reschedule jobs

        public ICollection<TransactionItem> TransactionItems { get; set; } = new List<TransactionItem>();
    }
}