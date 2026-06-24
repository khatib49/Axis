using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// AI-proposed action awaiting admin approval. Never executed
    /// automatically — admin must approve via the chat UI or actions page.
    /// </summary>
    [Table("PendingAiActions")]
    public class PendingAiAction
    {
        [Key] public int Id { get; set; }

        // 'FlashTournament' | 'CustomerPing' | 'WhatsAppBlast'
        [Required][MaxLength(50)] public string Type { get; set; } = default!;

        [Required][MaxLength(300)] public string Title { get; set; } = default!;
        public string? Summary { get; set; }

        // JSON describing the proposed action — schema depends on Type.
        [Required] public string Payload { get; set; } = "{}";

        // 'Pending' | 'Approved' | 'Rejected' | 'Executed' | 'Failed'
        [Required][MaxLength(20)] public string Status { get; set; } = "Pending";

        [MaxLength(200)] public string? ProposedBy { get; set; }
        public DateTime ProposedOn { get; set; } = DateTime.UtcNow;

        public int? ConversationId { get; set; }
        [ForeignKey(nameof(ConversationId))] public AiConversation? Conversation { get; set; }

        [MaxLength(200)] public string? DecidedBy { get; set; }
        public DateTime? DecidedOn { get; set; }

        public string? ExecutionLog { get; set; }
        public DateTime? ExecutedOn { get; set; }
    }
}
