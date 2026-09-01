using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A paid extra an item can offer — "Oat Milk +$1.00", "Extra Shot
    /// +$0.50". Fully managed from the admin Items screen; the cashier sees
    /// them in a customize sheet when adding the item to an order.
    /// </summary>
    [Table("ItemAddOns")]
    public class ItemAddOn
    {
        [Key] public int Id { get; set; }

        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;

        [Required][MaxLength(120)] public string Name { get; set; } = default!;

        [Column(TypeName = "numeric(18,2)")] public decimal Price { get; set; }

        /// <summary>Soft-hide instead of delete — historical order lines reference add-ons.</summary>
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// An add-on chosen on one order line. Name and price are SNAPSHOTTED at
    /// sale time — repricing or renaming the add-on later must not rewrite
    /// what old receipts say was charged.
    /// </summary>
    [Table("TransactionItemAddOns")]
    public class TransactionItemAddOn
    {
        [Key] public int Id { get; set; }

        // Composite FK to the order line (TransactionItems' key).
        public int TransactionRecordId { get; set; }
        public int ItemId { get; set; }
        public TransactionItem Line { get; set; } = default!;

        public int AddOnId { get; set; }
        public ItemAddOn AddOn { get; set; } = default!;

        [Required][MaxLength(120)] public string Name { get; set; } = default!;

        [Column(TypeName = "numeric(18,2)")] public decimal UnitPrice { get; set; }

        public int Quantity { get; set; } = 1;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
