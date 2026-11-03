using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A cashier's working session on a register: opened with a float, closed
    /// with a counted cash amount. One open shift per user per register.
    /// </summary>
    [Table("Till_CashierShifts")]
    public class CashierShift
    {
        public int Id { get; set; }
        public string RegisterName { get; set; } = default!;
        /// <summary>Existing AppUser id (no navigation; referenced by id only).</summary>
        public int UserId { get; set; }
        public string UserName { get; set; } = default!;
        public DateTime OpenedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedOn { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal OpeningFloat { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? ExpectedCash { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? CountedCash { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? Variance { get; set; }
        /// <summary>"Open" | "Closed".</summary>
        public string Status { get; set; } = ShiftStatuses.Open;
        public string? Notes { get; set; }
        public string? ClosedBy { get; set; }
        /// <summary>Frozen Z report (JSON) captured at close time.</summary>
        public string? ReportJson { get; set; }
        public int? BranchId { get; set; }

        public ICollection<CashMovement> Movements { get; set; } = new List<CashMovement>();
    }

    public static class ShiftStatuses
    {
        public const string Open = "Open";
        public const string Closed = "Closed";
    }
}
