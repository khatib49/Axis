using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Reusable shift definition (e.g. "Morning 08:00-16:00") used by the schedule palette.</summary>
    [Table("Hr_ShiftTemplates")]
    public class ShiftTemplate
    {
        [Key]
        public int Id { get; set; }
        [Required, MaxLength(100)]
        public string Name { get; set; } = default!;
        public TimeOnly StartTime { get; set; }
        /// <summary>When EndTime &lt;= StartTime the shift ends the next day.</summary>
        public TimeOnly EndTime { get; set; }
        [MaxLength(20)]
        public string? Color { get; set; }
    }
}
