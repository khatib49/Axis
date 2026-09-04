using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Editable copy, images and settings of the public website, stored as one
    /// JSON document per key ("website"). The dashboard's Website editor owns
    /// the document's shape; the API only stores and serves it.
    /// </summary>
    [Table("SiteContents")]
    public class SiteContent
    {
        [Key] public int Id { get; set; }

        [Required][MaxLength(60)] public string Key { get; set; } = default!;

        /// <summary>The JSON document. Images inside it are relative media paths.</summary>
        [Required] public string Json { get; set; } = "{}";

        [MaxLength(200)] public string? UpdatedBy { get; set; }
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
    }
}
