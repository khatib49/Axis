using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One execution of a ScheduledReport (scheduled or run-now).</summary>
    [Table("Bi_ReportRuns")]
    public class ReportRun
    {
        public int Id { get; set; }
        public int ScheduledReportId { get; set; }
        public ScheduledReport ScheduledReport { get; set; } = default!;
        public DateTime RanOn { get; set; } = DateTime.UtcNow;
        /// <summary>Sent | Partial | Skipped: ... | Failed: ...</summary>
        [MaxLength(300)] public string Status { get; set; } = "Pending";
        public string? Error { get; set; }
        /// <summary>First 2000 characters of the rendered report.</summary>
        [MaxLength(2000)] public string? PayloadPreview { get; set; }
        /// <summary>True when triggered from the UI rather than the hourly job.</summary>
        public bool IsManual { get; set; }
    }
}
