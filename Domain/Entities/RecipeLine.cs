using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One ingredient inside a menu item's recipe. A burger Item has one
    /// RecipeLine per ingredient (beef, bun, lettuce, ...). Quantity is in
    /// the parent Ingredient's Unit.
    ///
    /// Sales of the parent Item subtract (Quantity * sold qty) from each
    /// linked Ingredient's QuantityOnHand and write a StockMovement row.
    /// </summary>
    [Table("RecipeLines")]
    public class RecipeLine
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;

        [Required]
        public int IngredientId { get; set; }
        public Ingredient Ingredient { get; set; } = default!;

        // How much of the ingredient one unit of the menu item consumes.
        // E.g. 200 (grams of beef) for one burger.
        [Required]
        [Column(TypeName = "numeric(18,3)")]
        public decimal Quantity { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
