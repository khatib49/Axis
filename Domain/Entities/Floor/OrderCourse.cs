using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Course number (1..n) assigned to one item line of an invoice, with the "fired to kitchen" stamp.</summary>
    [Table("Floor_OrderCourses")]
    public class OrderCourse
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        public int ItemId { get; set; }
        public int CourseNumber { get; set; } = 1;
        public DateTime? FiredOn { get; set; }
        [MaxLength(256)] public string? FiredBy { get; set; }
    }
}
