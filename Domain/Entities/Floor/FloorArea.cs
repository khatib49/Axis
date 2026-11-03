using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A zone of the venue floor (Terrace, Main hall…) that groups dining tables.</summary>
    [Table("Floor_Areas")]
    public class FloorArea
    {
        public int Id { get; set; }
        [Required][MaxLength(80)] public string Name { get; set; } = default!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public int? BranchId { get; set; }

        public ICollection<DiningTable> Tables { get; set; } = new List<DiningTable>();
    }
}
