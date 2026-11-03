using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("StockOps_StockCountLines")]
    public class StockCountLine
    {
        [Key]
        public int Id { get; set; }

        public int StockCountId { get; set; }
        public StockCount StockCount { get; set; } = default!;

        public int IngredientId { get; set; }

        // System quantity at the location. Prefilled when the sheet is
        // created and refreshed at post time so the variance reflects the
        // stock at the moment the count is applied.
        [Column(TypeName = "numeric(18,3)")]
        public decimal ExpectedQty { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal? CountedQty { get; set; }

        // CountedQty - ExpectedQty, computed on post.
        [Column(TypeName = "numeric(18,3)")]
        public decimal Variance { get; set; }

        // Ingredient.BuyPricePerUnit snapshot taken on post.
        [Column(TypeName = "numeric(18,4)")]
        public decimal? UnitCost { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal VarianceValue { get; set; }
    }
}
