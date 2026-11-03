using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A saved audience definition. RulesJson is a JSON object with optional,
    /// AND-ed rules: lastVisitWithinDays, lastVisitOlderThanDays, minSpend,
    /// spendPeriodDays, minVisits, loyaltyTicketsMin, hasEmail, hasPhone, branchId.
    /// The audience is always evaluated at send / preview time, never stored.
    /// </summary>
    [Table("Mkt_Segments")]
    public class MktSegment
    {
        public int Id { get; set; }
        [Required][MaxLength(150)] public string Name { get; set; } = default!;
        public string RulesJson { get; set; } = "{}";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string CreatedBy { get; set; } = "system";
    }
}
