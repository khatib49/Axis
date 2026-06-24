using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("WhatsAppMessages")]
    public class WhatsAppMessage
    {
        [Key] public int Id { get; set; }

        public int? PendingActionId { get; set; }
        [ForeignKey(nameof(PendingActionId))] public PendingAiAction? PendingAction { get; set; }

        [Required][MaxLength(40)] public string RecipientPhone { get; set; } = default!;
        [MaxLength(200)] public string? RecipientName { get; set; }

        [MaxLength(80)] public string? TemplateName { get; set; }
        public string? MessageBody { get; set; }

        // 'Queued' | 'Sent' | 'Delivered' | 'Read' | 'Failed'
        [Required][MaxLength(20)] public string Status { get; set; } = "Queued";

        [MaxLength(100)] public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }

        public DateTime QueuedOn { get; set; } = DateTime.UtcNow;
        public DateTime? SentOn { get; set; }
    }
}
