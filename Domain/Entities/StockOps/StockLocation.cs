using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A physical place stock lives in (main store, bar, kitchen fridge...).
    /// The default location mirrors Ingredient.QuantityOnHand: every movement
    /// posted against it updates the ingredient and writes a StockMovement
    /// exactly like the existing purchase / waste / adjustment paths.
    /// Non-default locations only track quantities in StockOps_LocationStocks.
    /// </summary>
    [Table("StockOps_Locations")]
    public class StockLocation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = default!;

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = default!;

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;

        public int? BranchId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
