using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Acct_BankStatements")]
    public class BankStatement
    {
        [Key]
        public int Id { get; set; }

        public int BankAccountId { get; set; }

        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal OpeningBalance { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal ClosingBalance { get; set; }

        public DateTime ImportedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? ImportedBy { get; set; }

        /// <summary>Open | Reconciled</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Open";

        public DateTime? ReconciledOn { get; set; }

        public int? BranchId { get; set; }

        public ICollection<BankStatementLine> Lines { get; set; } = new List<BankStatementLine>();
    }
}
