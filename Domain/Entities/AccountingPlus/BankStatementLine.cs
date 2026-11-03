using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Acct_BankStatementLines")]
    public class BankStatementLine
    {
        [Key]
        public int Id { get; set; }

        public int StatementId { get; set; }

        public DateTime Date { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? Reference { get; set; }

        /// <summary>Signed: positive = money in, negative = money out.</summary>
        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }

        /// <summary>Existing JournalEntryLine id this statement line was matched to.</summary>
        public int? MatchedJournalEntryLineId { get; set; }

        /// <summary>Unmatched | Matched | Excluded</summary>
        [MaxLength(20)]
        public string MatchStatus { get; set; } = "Unmatched";

        public DateTime? MatchedOn { get; set; }

        [MaxLength(256)]
        public string? MatchedBy { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
