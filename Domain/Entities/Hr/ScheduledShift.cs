using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A planned shift for one employee on one day. StartOn/EndOn are UTC.</summary>
    [Table("Hr_ScheduledShifts")]
    public class ScheduledShift
    {
        public const string StatusPlanned = "Planned";
        public const string StatusConfirmed = "Confirmed";
        public const string StatusCancelled = "Cancelled";

        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateOnly Date { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public int? TemplateId { get; set; }
        [MaxLength(100)]
        public string? Role { get; set; }
        [MaxLength(500)]
        public string? Notes { get; set; }
        [MaxLength(20)]
        public string Status { get; set; } = StatusPlanned;
        [MaxLength(200)]
        public string CreatedBy { get; set; } = "system";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? BranchId { get; set; }
    }
}
