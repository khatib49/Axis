namespace Application.DTOs
{
    public record AiConversationSummaryDto(
        int Id,
        string Title,
        DateTime CreatedOn,
        DateTime LastMessageOn,
        string? CreatedBy
    );

    public record AiMessageDto(
        int Id,
        string Role,            // 'user' | 'assistant' | 'tool'
        string? Content,
        string? ToolCalls,      // JSON
        string? ToolCallId,
        string? ToolName,
        DateTime CreatedOn
    );

    public record AiSendMessageRequest(
        int? ConversationId,    // null = create new
        string Message
    );

    public record AiSendMessageResponse(
        int ConversationId,
        IReadOnlyList<AiMessageDto> NewMessages,   // messages added during this turn
        IReadOnlyList<int> ProposedActionIds       // pending actions surfaced this turn
    );
}
