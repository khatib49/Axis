using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Moves ingredients between two locations. Status: Draft | Posted | Cancelled.
    /// </summary>
    [Table("StockOps_StockTransfers")]
    public class StockTransfer
    {
        [Key]
        public int Id { get; set; }

        public int FromLocationId { get; set; }
        public int ToLocationId { get; set; }

        [MaxLength(50)]
        public string Reference { get; set; } = default!;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Draft";

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(200)]
        public string? CreatedBy { get; set; }

        public DateTime? PostedOn { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? BranchId { get; set; }

        public ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
    }
}
