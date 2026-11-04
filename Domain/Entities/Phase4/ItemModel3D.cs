using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// 3D model of a menu item. GLB serves the web viewer and Android AR,
    /// USDZ serves iPhone AR Quick Look. One row per item; the items table
    /// itself is not changed.
    /// </summary>
    [Table("Menu_ItemModels")]
    public class ItemModel3D
    {
        [Key] public int Id { get; set; }
        public int ItemId { get; set; }
        /// <summary>Relative path under wwwroot (media/models/...).</summary>
        [Required][MaxLength(500)] public string GlbPath { get; set; } = default!;
        [MaxLength(500)] public string? UsdzPath { get; set; }
        /// <summary>Optional still image shown while the model loads.</summary>
        [MaxLength(500)] public string? PosterPath { get; set; }
        public long GlbBytes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
        [Required][MaxLength(256)] public string UpdatedBy { get; set; } = "system";
    }
}
