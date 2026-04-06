namespace Application.DTOs
{
    public record TransactionAuditLogDto(
      int Id,
      int TransactionId,
      string ChangedBy,
      DateTime ChangedOn,
      string Action,
      string? FieldChanged,
      string? OldValue,
      string? NewValue,
      string? Notes
  );
}
