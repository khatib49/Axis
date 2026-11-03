using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A table with a printed QR code that opens the self-ordering page.</summary>
    [Table("SelfOrder_Tables")]
    public class SelfOrderTable
    {
        [Key] public int Id { get; set; }
        /// <summary>Short code printed inside the QR (unique).</summary>
        [Required][MaxLength(40)] public string Code { get; set; } = default!;
        [Required][MaxLength(120)] public string Label { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        public int? BranchId { get; set; }
    }
}
