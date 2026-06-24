using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Key/value store for integration credentials (Anthropic, WhatsApp, etc.).
    /// Rows flagged IsSecret are masked in API responses.
    /// </summary>
    [Table("IntegrationSettings")]
    public class IntegrationSetting
    {
        [Key] public int Id { get; set; }

        [Required][MaxLength(80)] public string Key { get; set; } = default!;
        public string? Value { get; set; }
        public bool IsSecret { get; set; }

        [MaxLength(500)] public string? Description { get; set; }
        [MaxLength(200)] public string? UpdatedBy { get; set; }
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
    }
}
