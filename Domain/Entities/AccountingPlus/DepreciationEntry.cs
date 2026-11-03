using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Acct_DepreciationEntries")]
    public class DepreciationEntry
    {
        [Key]
        public int Id { get; set; }

        public int AssetId { get; set; }

        public int PeriodYear { get; set; }
        public int PeriodMonth { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }

        public int? JournalEntryId { get; set; }

        public DateTime PostedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? PostedBy { get; set; }
    }
}
