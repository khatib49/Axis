using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("AiConversations")]
    public class AiConversation
    {
        [Key] public int Id { get; set; }

        [Required][MaxLength(200)] public string Title { get; set; } = "New chat";
        [MaxLength(200)] public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime LastMessageOn { get; set; } = DateTime.UtcNow;

        public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
    }
}
