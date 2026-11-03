using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Hr_LeaveTypes")]
    public class LeaveType
    {
        [Key]
        public int Id { get; set; }
        [Required, MaxLength(100)]
        public string Name { get; set; } = default!;
        public bool IsPaid { get; set; } = true;
        public int DefaultDaysPerYear { get; set; }
    }
}
