using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Delivery driver. Optionally linked to a staff AppUser so the driver can see "my orders".</summary>
    [Table("Delivery_Drivers")]
    public class Driver
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(120)] public string Name { get; set; } = default!;
        [MaxLength(40)] public string? Phone { get; set; }
        [MaxLength(200)] public string? VehicleNote { get; set; }
        /// <summary>Identity user id (no navigation, no FK) — lets a logged-in driver list his own orders.</summary>
        public int? UserId { get; set; }
        public bool IsActive { get; set; } = true;
        public int? BranchId { get; set; }
    }
}
