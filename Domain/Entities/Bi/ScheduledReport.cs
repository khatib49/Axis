using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A report the BI job builds and sends on a schedule. Hours are UTC; the
    /// hourly job sends every active report whose RunAtHourUtc matches.
    /// </summary>
    [Table("Bi_ScheduledReports")]
    public class ScheduledReport
    {
        public int Id { get; set; }
        [Required][MaxLength(150)] public string Name { get; set; } = default!;
        /// <summary>DailySales | WeeklySales | LowStock | OpenInvoices | ShiftSummary</summary>
        [Required][MaxLength(40)] public string ReportType { get; set; } = default!;
        /// <summary>Daily | Weekly | Monthly</summary>
        [Required][MaxLength(20)] public string Frequency { get; set; } = "Daily";
        /// <summary>0..23, UTC.</summary>
        public int RunAtHourUtc { get; set; } = 6;
        /// <summary>Weekly only: 0 = Sunday .. 6 = Saturday (System.DayOfWeek).</summary>
        public int? DayOfWeek { get; set; }
        /// <summary>Comma-separated e-mails and/or phone numbers.</summary>
        [MaxLength(2000)] public string Recipients { get; set; } = "";
        /// <summary>Email | WhatsApp | Both</summary>
        [Required][MaxLength(20)] public string Channel { get; set; } = "Email";
        /// <summary>Html | Csv</summary>
        [Required][MaxLength(10)] public string Format { get; set; } = "Html";
        public bool IsActive { get; set; } = true;
        public DateTime? LastRunOn { get; set; }
        [MaxLength(300)] public string? LastStatus { get; set; }
        [MaxLength(200)] public string CreatedBy { get; set; } = "system";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        /// <summary>Optional branch scope (no FK; multi-branch scoping later).</summary>
        public int? BranchId { get; set; }
    }
}
