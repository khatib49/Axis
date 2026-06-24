using Application.DTOs;

namespace Application.IServices
{
    public interface IAiChatService
    {
        Task<IReadOnlyList<AiConversationSummaryDto>> ListConversationsAsync(int take = 50, CancellationToken ct = default);
        Task<IReadOnlyList<AiMessageDto>> GetMessagesAsync(int conversationId, CancellationToken ct = default);
        Task DeleteConversationAsync(int conversationId, CancellationToken ct = default);

        Task<AiSendMessageResponse> SendAsync(AiSendMessageRequest req, string actor, CancellationToken ct = default);
    }
}
