using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Append-only audit log: every change to an Ingredient's stock writes
    /// one row here. Quantity is signed (+ for additions like Purchase,
    /// - for outflows like Consumption / Waste). Adjustments can be either.
    ///
    /// This is the source of truth; Ingredient.QuantityOnHand is a
    /// denormalized snapshot that can be rebuilt from these rows.
    /// </summary>
    [Table("StockMovements")]
    public class StockMovement
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int IngredientId { get; set; }
        public Ingredient Ingredient { get; set; } = default!;

        // Signed: positive = stock in, negative = stock out.
        [Required]
        [Column(TypeName = "numeric(18,3)")]
        public decimal Quantity { get; set; }

        // String-typed (not an enum) so the audit log stays human-readable
        // in raw SQL queries and future movement types don't require a
        // schema change. Validated in the service layer.
        // Allowed: "Purchase", "Consumption", "Waste", "Adjustment".
        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = default!;

        // Where this movement came from. For Consumption: "Transaction" +
        // TransactionRecord.Id. For manual events: typically null.
        [MaxLength(50)]
        public string? ReferenceType { get; set; }
        public int? ReferenceId { get; set; }

        // Free-text reason when Type=Waste (Spoilage, Spillage, Burnt,
        // Expired, Customer Return, Other).
        [MaxLength(100)]
        public string? WasteReason { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        // Snapshot of Ingredient.QuantityOnHand AFTER applying this
        // movement. Lets the audit page render running balances without a
        // recursive sum.
        [Required]
        [Column(TypeName = "numeric(18,3)")]
        public decimal BalanceAfter { get; set; }

        // Cost data snapshotted at the time of the movement.
        // For Purchase: the unit cost we paid (drives BuyPricePerUnit).
        // For Consumption: the unit cost the ingredient had at sale time
        // (drives COGS on the accounting dashboard).
        // Waste/Adjustment: optional; populated when known.
        [Column(TypeName = "numeric(18,4)")]
        public decimal? UnitCost { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal? TotalCost { get; set; }

        [MaxLength(200)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
