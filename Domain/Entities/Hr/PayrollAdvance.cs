using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Cash advance given to an employee; recovered (deducted) by the payroll run of the month it was given in.</summary>
    [Table("Hr_PayrollAdvances")]
    public class PayrollAdvance
    {
        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }
        public DateOnly On { get; set; }
        public int? RecoveredInRunId { get; set; }
        [MaxLength(500)]
        public string? Notes { get; set; }
        [MaxLength(200)]
        public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? BranchId { get; set; }
    }
}
