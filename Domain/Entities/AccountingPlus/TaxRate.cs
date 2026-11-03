using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Acct_TaxRates")]
    public class TaxRate
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = default!;

        [Column(TypeName = "numeric(5,2)")]
        public decimal RatePercent { get; set; }

        public bool IsDefault { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Sales | Purchases | Both</summary>
        [MaxLength(20)]
        public string AppliesTo { get; set; } = "Both";
    }
}
