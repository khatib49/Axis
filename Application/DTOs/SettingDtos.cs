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
        string CreatedBy, string? ModifiedBy , bool IsOffer
       );

    public record SettingCreateDto(string Name, string Type, int GameId, decimal Hours, decimal Price , bool IsOffer);
    public record SettingUpdateDto(string? Name, string? Type, int? GameId, decimal Hours, decimal Price , bool IsOffer);
}
