using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Informational lot / expiry layer over the existing purchases. Receiving
    /// a batch does NOT move stock (the purchase already did); writing an
    /// expired batch off DOES (Waste movement, reason "Expired").
    /// </summary>
    [Table("StockOps_StockBatches")]
    public class StockBatch
    {
        [Key]
        public int Id { get; set; }

        public int IngredientId { get; set; }
        public int? PurchaseLineId { get; set; }

        [MaxLength(100)]
        public string? LotNumber { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "numeric(18,3)")]
        public decimal RemainingQuantity { get; set; }

        public DateTime? ExpiresOn { get; set; }
        public DateTime ReceivedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? BranchId { get; set; }

        public DateTime? WrittenOffOn { get; set; }
    }
}
