using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A self-service kiosk screen. Token is sent as X-Kiosk-Token on every order.</summary>
    [Table("SelfOrder_KioskDevices")]
    public class KioskDevice
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(120)] public string Name { get; set; } = default!;
        [Required][MaxLength(64)] public string Token { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        public DateTime? LastSeenOn { get; set; }
        public int? BranchId { get; set; }
    }
}
