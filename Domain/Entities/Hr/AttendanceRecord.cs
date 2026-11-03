using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One clock-in / clock-out pair. ClockOut null = still clocked in. Times are UTC.</summary>
    [Table("Hr_AttendanceRecords")]
    public class AttendanceRecord
    {
        public const string SourcePin = "Pin";
        public const string SourceManual = "Manual";
        public const string SourceAdmin = "Admin";

        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime ClockIn { get; set; }
        public DateTime? ClockOut { get; set; }
        [MaxLength(20)]
        public string Source { get; set; } = SourcePin;
        public int? ScheduledShiftId { get; set; }
        [MaxLength(100)]
        public string? DeviceName { get; set; }
        [MaxLength(500)]
        public string? Notes { get; set; }
        /// <summary>Computed on clock out: whole minutes between ClockIn and ClockOut.</summary>
        public int? WorkedMinutes { get; set; }
        public int? BranchId { get; set; }
    }
}
