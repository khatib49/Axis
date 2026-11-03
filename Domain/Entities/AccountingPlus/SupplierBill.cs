using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Accounts-payable invoice from a supplier.</summary>
    [Table("Acct_SupplierBills")]
    public class SupplierBill
    {
        [Key]
        public int Id { get; set; }

        public int SupplierId { get; set; }

        [Required, MaxLength(100)]
        public string BillNumber { get; set; } = default!;

        public DateTime BillDate { get; set; }
        public DateTime DueDate { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal TaxAmount { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal Total { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal PaidAmount { get; set; }

        /// <summary>Draft | Open | PartiallyPaid | Paid | Void</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Draft";

        /// <summary>Optional link to an existing stock Purchase.</summary>
        public int? PurchaseId { get; set; }

        public int ExpenseGlAccountId { get; set; }
        public int PayableGlAccountId { get; set; }

        /// <summary>Existing JournalEntry posted when the bill was opened.</summary>
        public int? JournalEntryId { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public int? BranchId { get; set; }

        public ICollection<SupplierBillPayment> Payments { get; set; } = new List<SupplierBillPayment>();
    }
}
