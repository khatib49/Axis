using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Cash paid into or taken out of the drawer during a shift.</summary>
    [Table("Till_CashMovements")]
    public class CashMovement
    {
        public int Id { get; set; }
        public int ShiftId { get; set; }
        public CashierShift Shift { get; set; } = default!;
        /// <summary>"PayIn" | "PayOut".</summary>
        public string Type { get; set; } = default!;
        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }
        public string Reason { get; set; } = default!;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = default!;
    }

    public static class CashMovementTypes
    {
        public const string PayIn = "PayIn";
        public const string PayOut = "PayOut";
    }
}
