using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Units of <see cref="CurrencyCode"/> per 1 unit of the base currency, from a given date.</summary>
    [Table("Currency_ExchangeRates")]
    public class ExchangeRate
    {
        public int Id { get; set; }
        public string CurrencyCode { get; set; } = default!;
        public Currency Currency { get; set; } = default!;
        [Column(TypeName = "numeric(18,6)")]
        public decimal Rate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = default!;
    }
}
