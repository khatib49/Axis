using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Optional per-ingredient reorder policy. When absent the reorder
    /// suggestions fall back to Ingredient.ReorderLevel (target = 2x level).
    /// </summary>
    [Table("StockOps_ReorderRules")]
    public class ReorderRule
    {
        [Key]
        public int Id { get; set; }

        public int IngredientId { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal MinQty { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal TargetQty { get; set; }

        public int? PreferredSupplierId { get; set; }

        public int LeadTimeDays { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
