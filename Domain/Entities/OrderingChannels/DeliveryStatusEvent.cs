using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Audit trail of every status change on a delivery / takeaway order.</summary>
    [Table("Delivery_StatusEvents")]
    public class DeliveryStatusEvent
    {
        [Key] public int Id { get; set; }
        public int DeliveryOrderId { get; set; }
        [MaxLength(30)] public string? FromStatus { get; set; }
        [Required][MaxLength(30)] public string ToStatus { get; set; } = default!;
        public DateTime On { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(200)] public string By { get; set; } = "system";
        [MaxLength(500)] public string? Note { get; set; }

        public DeliveryOrder DeliveryOrder { get; set; } = default!;
    }
}
