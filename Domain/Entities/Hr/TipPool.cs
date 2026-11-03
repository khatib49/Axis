using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Tips collected on one day, split across staff by Equal / ByHours / ByPoints.</summary>
    [Table("Hr_TipPools")]
    public class TipPool
    {
        public const string MethodEqual = "Equal";
        public const string MethodByHours = "ByHours";
        public const string MethodByPoints = "ByPoints";
        public const string StatusDraft = "Draft";
        public const string StatusDistributed = "Distributed";

        [Key]
        public int Id { get; set; }
        public DateOnly Date { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalAmount { get; set; }
        [MaxLength(20)]
        public string Method { get; set; } = MethodEqual;
        [MaxLength(20)]
        public string Status { get; set; } = StatusDraft;
        [MaxLength(500)]
        public string? Notes { get; set; }
        [MaxLength(200)]
        public string CreatedBy { get; set; } = "system";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? DistributedOn { get; set; }
        public int? BranchId { get; set; }

        public ICollection<TipPoolAllocation> Allocations { get; set; } = new List<TipPoolAllocation>();
    }
}
