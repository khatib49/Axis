using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One part of a split bill. "Remaining" is computed by the service (TotalPrice − paid splits).</summary>
    [Table("Floor_BillSplits")]
    public class BillSplit
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        public int SplitNumber { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
        /// <summary>Cash | Card | Wallet | Other.</summary>
        [MaxLength(20)] public string Method { get; set; } = "Cash";
        public DateTime? PaidOn { get; set; }
        [MaxLength(256)] public string? PaidBy { get; set; }
        [MaxLength(300)] public string? Note { get; set; }
        public int? BranchId { get; set; }
    }
}
