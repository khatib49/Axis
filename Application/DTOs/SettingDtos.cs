namespace Application.DTOs
{
    public record SettingsAttributeDto(int Id, string Name, string AttributeValue, int SettingsId);
    public record SettingsAttributeCreateDto(string Name, string AttributeValue, int SettingsId);
    public record SettingsAttributeUpdateDto(string? Name, string? AttributeValue);

    public record SettingsValueDto(int Id, int SettingsId, int AttributeId, string Value);
    public record SettingsValueCreateDto(int SettingsId, int AttributeId, string Value);
    public record SettingsValueUpdateDto(int? AttributeId, string? Value);

    public record SettingDto(
        int Id, string Name, string Type, int GameId, string GameName,decimal Hours, decimal Price, DateTime CreatedOn, DateTime? ModifiedOn,
        string CreatedBy, string? ModifiedBy , bool IsOffer, bool IsOpenHour , bool IsDayPass,
        // Hidden / soft-deleted settings have IsActive=false. The UI uses this
        // to render a "Hidden" badge and to let admins restore via the toggle
        // in the edit modal.
        bool IsActive = true
       );

    public record SettingCreateDto(string Name, string Type, int GameId, decimal Hours, decimal Price , bool IsOffer, bool IsOpenHour , bool IsDayPass);
    public record SettingUpdateDto(string? Name, string? Type, int? GameId, decimal Hours, decimal Price , bool IsOffer, bool IsOpenHour , bool IsDayPass, bool? IsActive = null);
}
