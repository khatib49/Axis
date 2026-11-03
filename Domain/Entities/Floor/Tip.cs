using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A gratuity recorded against an invoice, optionally attributed to a staff member.</summary>
    [Table("Floor_Tips")]
    public class Tip
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
        /// <summary>Cash | Card | Wallet | Other.</summary>
        [MaxLength(20)] public string Method { get; set; } = "Cash";
        /// <summary>Existing AppUser id of the staff member (no navigation).</summary>
        public int? StaffUserId { get; set; }
        [MaxLength(256)] public string? StaffName { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = default!;
        public int? BranchId { get; set; }
    }
}
