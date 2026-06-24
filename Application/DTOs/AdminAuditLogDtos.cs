namespace Application.DTOs
{
    public record AdminAuditLogDto(
        int Id,
        string EntityType,
        int? EntityId,
        string? EntityName,
        string Action,
        string? FieldChanges,   // raw JSON; FE decodes for display
        string? Snapshot,       // raw JSON
        string? ChangedBy,
        DateTime ChangedOn
    );

    public record AdminAuditFilterDto(
        string? EntityType,
        string? Action,
        string? ChangedBy,
        int? EntityId,
        DateTime? From,
        DateTime? To,
        int Page = 1,
        int PageSize = 50
    );
}
