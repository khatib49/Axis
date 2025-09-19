namespace Application.DTOs
{
    public record SettingsAttributeDto(Guid Id, string Name, string AttributeValue, Guid SettingsId);
    public record SettingsAttributeCreateDto(string Name, string AttributeValue, Guid SettingsId);
    public record SettingsAttributeUpdateDto(string? Name, string? AttributeValue);

    public record SettingsValueDto(Guid Id, Guid SettingsId, Guid AttributeId, string Value);
    public record SettingsValueCreateDto(Guid SettingsId, Guid AttributeId, string Value);
    public record SettingsValueUpdateDto(Guid? AttributeId, string? Value);

    public record SettingDto(
        Guid Id, string Name, string Type, Guid GameId, string GameName,
        List<SettingsAttributeDto> Attributes, List<SettingsValueDto> Values);

    public record SettingCreateDto(string Name, string Type, Guid GameId);
    public record SettingUpdateDto(string? Name, string? Type, Guid? GameId);
}
