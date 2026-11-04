using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Per-item switch: when RequiresSerial is on, the till must record which
    /// serial number left the shop on every sale of that item. Prefix is used
    /// when the shop generates its own serials on receipt ("SLV-0010-00001").
    /// One row per item (unique ItemId); absent row = no serial tracking.
    /// </summary>
    [Table("Retail_SerialSettings")]
    public class SerialSetting
    {
        [Key] public int Id { get; set; }
        public int ItemId { get; set; }
        public bool RequiresSerial { get; set; }
        [MaxLength(20)] public string? Prefix { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        [MaxLength(256)] public string ModifiedBy { get; set; } = "system";
    }
}
