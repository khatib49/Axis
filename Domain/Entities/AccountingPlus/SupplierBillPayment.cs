using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Acct_SupplierBillPayments")]
    public class SupplierBillPayment
    {
        [Key]
        public int Id { get; set; }

        public int BillId { get; set; }

        public DateTime PaidOn { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(50)]
        public string? Method { get; set; }

        [MaxLength(100)]
        public string? Reference { get; set; }

        public int CashGlAccountId { get; set; }

        public int? JournalEntryId { get; set; }

        [MaxLength(256)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
