namespace Application.DTOs
{
    public record PendingActionDto(
        int Id,
        string Type,
        string Title,
        string? Summary,
        string Payload,         // raw JSON; FE parses for friendly preview
        string Status,
        string? ProposedBy,
        DateTime ProposedOn,
        int? ConversationId,
        string? DecidedBy,
        DateTime? DecidedOn,
        string? ExecutionLog,
        DateTime? ExecutedOn
    );

    public record PendingActionsFilterDto(
        string? Status = null,    // 'Pending' (default), 'Approved', 'Rejected', 'Executed', 'Failed', or null = all
        int Page = 1,
        int PageSize = 50
    );

    public record ActionDecisionResultDto(
        bool Ok,
        string Status,
        string? Message
    );
}
