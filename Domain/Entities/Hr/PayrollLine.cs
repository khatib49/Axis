using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One employee's pay in a PayrollRun. ComponentsJson holds the allowance/deduction breakdown.</summary>
    [Table("Hr_PayrollLines")]
    public class PayrollLine
    {
        [Key]
        public int Id { get; set; }
        public int RunId { get; set; }
        public int EmployeeId { get; set; }
        [MaxLength(150)]
        public string EmployeeName { get; set; } = "";
        [MaxLength(100)]
        public string? Position { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal BasePay { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal HoursWorked { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? HourlyRate { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? MonthlySalary { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal OvertimeHours { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal OvertimePay { get; set; }
        /// <summary>Component allowances + ManualAllowance.</summary>
        [Column(TypeName = "numeric(18,2)")]
        public decimal Allowances { get; set; }
        /// <summary>Component deductions + advances recovered + ManualDeduction.</summary>
        [Column(TypeName = "numeric(18,2)")]
        public decimal Deductions { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal StatutoryDeductions { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal AdvancesRecovered { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal ManualAllowance { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal ManualDeduction { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal Gross { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal Net { get; set; }
        public string? ComponentsJson { get; set; }
        [MaxLength(500)]
        public string? Notes { get; set; }
        public bool IsExcluded { get; set; }
    }
}
