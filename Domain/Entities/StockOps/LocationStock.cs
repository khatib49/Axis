using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Quantity of an ingredient held at a NON-default location. The default
    /// location never has rows here: its quantity is Ingredient.QuantityOnHand.
    /// </summary>
    [Table("StockOps_LocationStocks")]
    public class LocationStock
    {
        [Key]
        public int Id { get; set; }

        public int LocationId { get; set; }
        public int IngredientId { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal Quantity { get; set; }

        public DateTime? ModifiedOn { get; set; }
    }
}
