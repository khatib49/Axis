using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A tender currency. Exactly one row is the base (pricing) currency.</summary>
    [Table("Currency_Currencies")]
    public class Currency
    {
        /// <summary>ISO-4217 style code, e.g. "USD", "LBP".</summary>
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string Symbol { get; set; } = default!;
        public int DecimalPlaces { get; set; } = 2;
        public bool IsBase { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Cash rounding step in this currency, e.g. 1000 for LBP, 0.01 for USD.</summary>
        [Column(TypeName = "numeric(18,4)")]
        public decimal RoundingStep { get; set; } = 0.01m;
    }
}
