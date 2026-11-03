using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Per-branch override (receipt header, tax number, ...).</summary>
    [Table("Branch_Settings")]
    public class BranchSetting
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public Branch Branch { get; set; } = default!;
        [Required][MaxLength(80)] public string Key { get; set; } = default!;
        public string? Value { get; set; }
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string? UpdatedBy { get; set; }
    }
}
