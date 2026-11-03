using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A physical venue / location. Existing transactions carry no branch yet;
    /// new modules store a nullable BranchId that points here.
    /// </summary>
    [Table("Branch_Branches")]
    public class Branch
    {
        public int Id { get; set; }
        [Required][MaxLength(20)] public string Code { get; set; } = default!;
        [Required][MaxLength(150)] public string Name { get; set; } = default!;
        [MaxLength(300)] public string? Address { get; set; }
        [MaxLength(40)] public string? Phone { get; set; }
        [MaxLength(3)] public string CurrencyCode { get; set; } = "USD";
        [MaxLength(60)] public string TimeZoneId { get; set; } = "Asia/Beirut";
        public bool IsActive { get; set; } = true;
        /// <summary>Exactly one branch should be the default; legacy data is attributed to it.</summary>
        public bool IsDefault { get; set; }
        public TimeSpan? OpeningTime { get; set; }
        public TimeSpan? ClosingTime { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
