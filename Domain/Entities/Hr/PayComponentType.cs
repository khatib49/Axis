using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A payroll allowance or deduction definition (e.g. NSSF employee 3%, transport allowance).
    /// Rate is a fixed amount for Method "Fixed" or a percentage (0-100) for "PercentOfGross".
    /// DefaultForAll applies the component to every employee unless an EmployeePayComponent overrides it.
    /// </summary>
    [Table("Hr_PayComponentTypes")]
    public class PayComponentType
    {
        public const string KindAllowance = "Allowance";
        public const string KindDeduction = "Deduction";
        public const string MethodFixed = "Fixed";
        public const string MethodPercentOfGross = "PercentOfGross";

        [Key]
        public int Id { get; set; }
        [Required, MaxLength(100)]
        public string Name { get; set; } = default!;
        [MaxLength(20)]
        public string Kind { get; set; } = KindDeduction;
        [MaxLength(20)]
        public string Method { get; set; } = MethodFixed;
        [Column(TypeName = "numeric(18,4)")]
        public decimal Rate { get; set; }
        public bool IsStatutory { get; set; }
        public bool IsActive { get; set; } = true;
        public bool DefaultForAll { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
