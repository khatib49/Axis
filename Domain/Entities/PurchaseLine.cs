using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("PurchaseLines")]
    public class PurchaseLine
    {
        [Key]
        public int Id { get; set; }

        public int PurchaseId { get; set; }
        public Purchase Purchase { get; set; } = default!;

        public int IngredientId { get; set; }
        public Ingredient Ingredient { get; set; } = default!;

        [Column(TypeName = "numeric(18,3)")]
        public decimal Quantity { get; set; }

        // Cost per unit at the time of this shipment. Updates the parent
        // ingredient's BuyPricePerUnit so future sales are costed at the
        // latest known price (the "Latest cost" method).
        [Column(TypeName = "numeric(18,4)")]
        public decimal UnitCost { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal LineTotal { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
