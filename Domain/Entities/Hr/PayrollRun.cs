using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One payroll run per calendar month. Draft → Approved (journal posted) → Paid (cash entry posted).</summary>
    [Table("Hr_PayrollRuns")]
    public class PayrollRun
    {
        public const string StatusDraft = "Draft";
        public const string StatusApproved = "Approved";
        public const string StatusPaid = "Paid";

        [Key]
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        [MaxLength(20)]
        public string Status { get; set; } = StatusDraft;
        public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(200)]
        public string? GeneratedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        [MaxLength(200)]
        public string? ApprovedBy { get; set; }
        public DateTime? PaidOn { get; set; }
        [MaxLength(200)]
        public string? PaidBy { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalGross { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalDeductions { get; set; }
        /// <summary>Part of TotalDeductions that is statutory (credited to the statutory payable account).</summary>
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalStatutory { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalNet { get; set; }
        public int? JournalEntryId { get; set; }
        public int? PaymentJournalEntryId { get; set; }
        [MaxLength(1000)]
        public string? Notes { get; set; }
        public int? BranchId { get; set; }

        public ICollection<PayrollLine> Lines { get; set; } = new List<PayrollLine>();
    }
}
