using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Delivery area with its fee / minimum order (OrderingChannels module).</summary>
    [Table("Delivery_Zones")]
    public class DeliveryZone
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(120)] public string Name { get; set; } = default!;
        [Column(TypeName = "numeric(18,2)")] public decimal Fee { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal MinOrder { get; set; }
        public int EtaMinutes { get; set; } = 30;
        public bool IsActive { get; set; } = true;
        public int? BranchId { get; set; }
    }
}
