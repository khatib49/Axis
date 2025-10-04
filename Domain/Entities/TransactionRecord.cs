using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("transactions")]
    public class TransactionRecord
    {
        public Guid Id { get; set; }

       
        public Guid? RoomId { get; set; }
        public Room? Room { get; set; }  

        public Guid? GameTypeId { get; set; }
        public Category? GameType { get; set; }  

        public Guid? GameId { get; set; }
        public Game? Game { get; set; } 

        public Guid? GameSettingId { get; set; }
        public Setting? GameSetting { get; set; }

        // Required fields
        public Guid StatusId { get; set; }
        public Status Status { get; set; } = default!; 

        // Transaction details
        public int Hours { get; set; }
        public decimal TotalPrice { get; set; }

        // Audit fields
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public string CreatedBy { get; set; } = default!;  

        public ICollection<TransactionItem> TransactionItems { get; set; } = new List<TransactionItem>();
    }
}