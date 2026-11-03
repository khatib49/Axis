using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("StockOps_StockTransferLines")]
    public class StockTransferLine
    {
        [Key]
        public int Id { get; set; }

        public int TransferId { get; set; }
        public StockTransfer Transfer { get; set; } = default!;

        public int IngredientId { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal Quantity { get; set; }
    }
}
