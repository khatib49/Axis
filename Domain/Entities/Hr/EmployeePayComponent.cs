using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Per-employee override of a PayComponentType: Amount replaces a fixed rate, Rate replaces a
    /// percentage. IsActive=false opts the employee out of a default-for-all component.
    /// </summary>
    [Table("Hr_EmployeePayComponents")]
    public class EmployeePayComponent
    {
        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int ComponentTypeId { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? Amount { get; set; }
        [Column(TypeName = "numeric(18,4)")]
        public decimal? Rate { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
    }
}
