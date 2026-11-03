using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Course number (1..n) and seat assignment of one item line of an invoice, with the "fired to kitchen" stamp.
    /// One row per (invoice, item, seat); the invoice line itself (TransactionItem) keeps the total quantity.
    /// </summary>
    [Table("Floor_OrderCourses")]
    public class OrderCourse
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        public int ItemId { get; set; }
        /// <summary>Seat (1..GuestCount) this portion belongs to; null = shared by the table / not assigned.</summary>
        public int? SeatNumber { get; set; }
        /// <summary>How many units of the invoice line belong to this seat (the invoice line holds the total).</summary>
        public int Quantity { get; set; } = 1;
        public int CourseNumber { get; set; } = 1;
        public DateTime? FiredOn { get; set; }
        [MaxLength(256)] public string? FiredBy { get; set; }
    }
}
