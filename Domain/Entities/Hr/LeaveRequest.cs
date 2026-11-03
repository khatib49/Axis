using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Hr_LeaveRequests")]
    public class LeaveRequest
    {
        public const string StatusPending = "Pending";
        public const string StatusApproved = "Approved";
        public const string StatusRejected = "Rejected";
        public const string StatusCancelled = "Cancelled";

        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int LeaveTypeId { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        /// <summary>Calendar days inclusive (ToDate - FromDate + 1).</summary>
        public int Days { get; set; }
        [MaxLength(1000)]
        public string? Reason { get; set; }
        [MaxLength(20)]
        public string Status { get; set; } = StatusPending;
        public DateTime RequestedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(200)]
        public string? DecidedBy { get; set; }
        public DateTime? DecidedOn { get; set; }
        [MaxLength(1000)]
        public string? DecisionNote { get; set; }
    }
}
