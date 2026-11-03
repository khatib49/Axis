using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A restaurant table on the floor plan. Position/size are grid units for the visual layout.</summary>
    [Table("Floor_Tables")]
    public class DiningTable
    {
        public int Id { get; set; }
        public int AreaId { get; set; }
        public FloorArea Area { get; set; } = default!;
        [Required][MaxLength(20)] public string Code { get; set; } = default!;
        [Required][MaxLength(80)] public string Name { get; set; } = default!;
        public int Capacity { get; set; } = 4;
        /// <summary>"Rect" or "Round".</summary>
        [MaxLength(10)] public string Shape { get; set; } = "Rect";
        public int PosX { get; set; }
        public int PosY { get; set; }
        public int Width { get; set; } = 2;
        public int Height { get; set; } = 2;
        public bool IsActive { get; set; } = true;
        public int? BranchId { get; set; }
    }
}
