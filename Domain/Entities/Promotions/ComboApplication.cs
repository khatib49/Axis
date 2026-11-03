using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// What the automatic promotion pass currently books for one combo on one
    /// open invoice: how many whole combo sets the lines contain and the total
    /// saving applied for them. One row per (ComboId, TransactionId); the row is
    /// re-evaluated (Quantity / Amount updated, Version bumped) every time the
    /// invoice lines change, so the invoice is never discounted twice.
    /// </summary>
    [Table("Promo_ComboApplications")]
    public class ComboApplication
    {
        public int Id { get; set; }

        public int ComboId { get; set; }
        public Combo Combo { get; set; } = default!;

        /// <summary>The existing invoice (transactions.Id).</summary>
        public int TransactionId { get; set; }

        /// <summary>Whole combo sets currently found on the invoice.</summary>
        public int Quantity { get; set; }

        /// <summary>Total saving currently applied for this combo (positive number).</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }

        /// <summary>Incremented on every re-evaluation that changed Amount; part of the adjuster idempotency key.</summary>
        public int Version { get; set; }

        public DateTime On { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string By { get; set; } = "system";

        public int? BranchId { get; set; }
    }
}
