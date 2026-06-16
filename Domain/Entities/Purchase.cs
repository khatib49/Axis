using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A single shipment received from a supplier (or recorded without
    /// one). Has multiple PurchaseLine rows, each tied to an Ingredient
    /// with quantity + unit cost. Creating a Purchase:
    ///   - adds quantity to each Ingredient.QuantityOnHand
    ///   - writes a StockMovement (Type=Purchase) per line with cost data
    ///   - updates each Ingredient.BuyPricePerUnit to the latest unit cost
    /// </summary>
    [Table("Purchases")]
    public class Purchase
    {
        [Key]
        public int Id { get; set; }

        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }

        public DateTime PurchaseDate { get; set; }

        [MaxLength(100)]
        public string? InvoiceNumber { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalCost { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [MaxLength(200)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public ICollection<PurchaseLine> Lines { get; set; } = new List<PurchaseLine>();
    }
}
