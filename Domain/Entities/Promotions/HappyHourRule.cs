using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Time-based pricing rule: on the listed weekdays, between StartTime and
    /// EndTime (venue local time, Asia/Beirut), matching items are
    /// DiscountPercent cheaper. StartTime &gt; EndTime means the window crosses
    /// midnight (22:00 → 02:00) and the day-of-week is the one the window
    /// STARTED on.
    /// </summary>
    [Table("Promo_HappyHourRules")]
    public class HappyHourRule
    {
        public int Id { get; set; }

        [Required][MaxLength(150)] public string Name { get; set; } = default!;

        /// <summary>ISO weekdays, comma separated: 1 = Monday … 7 = Sunday. Empty = every day.</summary>
        [MaxLength(30)] public string DaysOfWeekCsv { get; set; } = "";

        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }

        [Column(TypeName = "numeric(5,2)")] public decimal DiscountPercent { get; set; }

        /// <summary>Comma separated Category ids; null/empty + empty ItemIdsCsv = every item.</summary>
        [MaxLength(500)] public string? CategoryIdsCsv { get; set; }
        /// <summary>Comma separated Item ids.</summary>
        [MaxLength(1000)] public string? ItemIdsCsv { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Higher wins when several rules match the same item.</summary>
        public int Priority { get; set; }

        public int? BranchId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Record that happy hour has been applied to an invoice — one per
    /// invoice (unique TransactionId) so a double click can't discount twice.
    /// </summary>
    [Table("Promo_HappyHourApplications")]
    public class HappyHourApplication
    {
        public int Id { get; set; }

        public int RuleId { get; set; }
        public HappyHourRule Rule { get; set; } = default!;

        /// <summary>The existing invoice (transactions.Id). Unique.</summary>
        public int TransactionId { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }

        public DateTime On { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string By { get; set; } = "system";
    }
}
