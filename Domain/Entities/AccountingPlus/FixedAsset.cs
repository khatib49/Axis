using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Acct_FixedAssets")]
    public class FixedAsset
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = default!;

        [MaxLength(100)]
        public string? Category { get; set; }

        public DateTime AcquiredOn { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal Cost { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal SalvageValue { get; set; }

        public int UsefulLifeMonths { get; set; }

        /// <summary>StraightLine | DecliningBalance</summary>
        [MaxLength(20)]
        public string Method { get; set; } = "StraightLine";

        public int AssetGlAccountId { get; set; }
        public int AccumulatedDepGlAccountId { get; set; }
        public int DepExpenseGlAccountId { get; set; }

        /// <summary>Active | Disposed</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        public DateTime? DisposedOn { get; set; }

        [Column(TypeName = "numeric(18,2)")]
        public decimal? DisposalProceeds { get; set; }

        public int? DisposalJournalEntryId { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? BranchId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public ICollection<DepreciationEntry> DepreciationEntries { get; set; } = new List<DepreciationEntry>();
    }
}
