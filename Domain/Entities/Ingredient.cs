using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A raw material consumed by recipes (Beef, Lettuce, Coke Can, ...).
    /// Backed by a manually-created DB table; see the ALTER script delivered
    /// with the stock-management CR. QuantityOnHand is denormalized for
    /// fast reads — the authoritative history lives in StockMovements and
    /// can rebuild this value from scratch.
    /// </summary>
    [Table("Ingredients")]
    public class Ingredient
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = default!;

        // Free-text unit ("g", "ml", "pcs", "kg", "l"). Each ingredient has
        // one canonical unit; recipe lines are stored in that same unit.
        [Required]
        [MaxLength(20)]
        public string Unit { get; set; } = "g";

        [Column(TypeName = "numeric(18,3)")]
        public decimal QuantityOnHand { get; set; } = 0;

        // Optional low-stock threshold. Ingredients below this show on the
        // chef's Low Stock Alerts banner.
        [Column(TypeName = "numeric(18,3)")]
        public decimal? ReorderLevel { get; set; }

        // Optional unit cost — wired later when we add automatic COGS
        // calculations to the accounting dashboard.
        [Column(TypeName = "numeric(18,4)")]
        public decimal? BuyPricePerUnit { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        // Inverse navigations
        public ICollection<RecipeLine> RecipeLines { get; set; } = new List<RecipeLine>();
        public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
    }
}
