using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A bundle of existing items sold at one price. Selling a combo adds its
    /// lines to the invoice through the normal order path (stock, kitchen
    /// tickets, recipes all behave as usual) and then books the difference
    /// between the combo price and the sum of the line prices as a negative
    /// adjustment when the combo is cheaper.
    /// </summary>
    [Table("Promo_Combos")]
    public class Combo
    {
        public int Id { get; set; }

        [Required][MaxLength(150)] public string Name { get; set; } = default!;
        [MaxLength(500)] public string? Description { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal Price { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(500)] public string? ImagePath { get; set; }

        public int? BranchId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public ICollection<ComboItem> Items { get; set; } = new List<ComboItem>();
    }

    [Table("Promo_ComboItems")]
    public class ComboItem
    {
        public int Id { get; set; }

        public int ComboId { get; set; }
        public Combo Combo { get; set; } = default!;

        /// <summary>Existing Item id (FK configured from this side only).</summary>
        public int ItemId { get; set; }

        public int Quantity { get; set; } = 1;
    }
}
