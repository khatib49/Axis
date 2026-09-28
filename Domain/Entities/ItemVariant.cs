using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A colour / type option of an item that carries ITS OWN stock —
    /// "Sleeves: Black (40), Green (12)". Price is the item's price plus an
    /// optional delta. Managed on the admin Items screen; the cashier and
    /// the website pick the option when adding the item.
    ///
    /// When an item has active variants, Item.Quantity is kept equal to the
    /// sum of the variants' quantities so every existing stock screen keeps
    /// working.
    /// </summary>
    [Table("ItemVariants")]
    public class ItemVariant
    {
        [Key] public int Id { get; set; }

        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;

        [Required][MaxLength(80)] public string Name { get; set; } = default!;
        /// <summary>Optional swatch for the UI, e.g. "#000000" or "green".</summary>
        [MaxLength(30)] public string? Color { get; set; }
        [MaxLength(60)] public string? Sku { get; set; }

        /// <summary>Added to the item price for this option (can be negative).</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal PriceDelta { get; set; }

        public int Quantity { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Which options were sold on one order line — "3 × Sleeves" becomes
    /// Black 2 + Green 1. Name and price delta are SNAPSHOTTED. Stock is
    /// deducted per option when the line is written.
    /// </summary>
    [Table("TransactionItemVariants")]
    public class TransactionItemVariant
    {
        [Key] public int Id { get; set; }

        public int TransactionRecordId { get; set; }
        public int ItemId { get; set; }
        public TransactionItem Line { get; set; } = default!;

        public int VariantId { get; set; }
        public ItemVariant Variant { get; set; } = default!;

        [Required][MaxLength(80)] public string Name { get; set; } = default!;
        [Column(TypeName = "numeric(18,2)")] public decimal PriceDelta { get; set; }
        public int Quantity { get; set; } = 1;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
