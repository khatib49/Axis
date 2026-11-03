using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A stocktake sheet for one location. Status: Draft | Posted | Cancelled.
    /// Posting computes the variance per line and applies it to stock.
    /// </summary>
    [Table("StockOps_StockCounts")]
    public class StockCount
    {
        [Key]
        public int Id { get; set; }

        public int LocationId { get; set; }

        [MaxLength(50)]
        public string Reference { get; set; } = default!;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Draft";

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(200)]
        public string? CreatedBy { get; set; }

        public DateTime? PostedOn { get; set; }

        [MaxLength(200)]
        public string? PostedBy { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? BranchId { get; set; }

        public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
    }
}
