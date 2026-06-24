using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("AiMessages")]
    public class AiMessage
    {
        [Key] public int Id { get; set; }

        public int ConversationId { get; set; }
        [ForeignKey(nameof(ConversationId))] public AiConversation? Conversation { get; set; }

        // 'user' | 'assistant' | 'tool'
        [Required][MaxLength(20)] public string Role { get; set; } = "user";

        public string? Content { get; set; }

        // JSON: tool calls the assistant emitted, or extra metadata
        public string? ToolCalls { get; set; }

        // For role='tool' responses — which call we're replying to
        [MaxLength(100)] public string? ToolCallId { get; set; }
        [MaxLength(100)] public string? ToolName { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
