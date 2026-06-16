using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A vendor we buy ingredients from. Backed by a manually-created DB
    /// table; see the v2 stock SQL script.
    /// </summary>
    [Table("Suppliers")]
    public class Supplier
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = default!;

        [MaxLength(500)]
        public string? ContactInfo { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    }
}
