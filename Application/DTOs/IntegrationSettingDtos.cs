namespace Application.DTOs
{
    // Secrets are masked in lists — only `••••••••` + last 4 chars are returned.
    public record IntegrationSettingDto(
        int Id,
        string Key,
        string? Value,           // masked if IsSecret + IsSet
        bool IsSecret,
        bool IsSet,              // true if a non-empty value is stored
        string? Description,
        string? UpdatedBy,
        DateTime UpdatedOn
    );

    public record IntegrationSettingUpdateDto(
        string Key,
        string? Value            // null/empty clears; otherwise saved verbatim
    );

    public record IntegrationTestResultDto(
        bool Ok,
        string? Message
    );
}
