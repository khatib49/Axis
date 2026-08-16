namespace Application.DTOs
{
    public record SettingsAttributeDto(int Id, string Name, string AttributeValue, int SettingsId);
    public record SettingsAttributeCreateDto(string Name, string AttributeValue, int SettingsId);
    public record SettingsAttributeUpdateDto(string? Name, string? AttributeValue);

    public record SettingsValueDto(int Id, int SettingsId, int AttributeId, string Value);
    public record SettingsValueCreateDto(int SettingsId, int AttributeId, string Value);
    public record SettingsValueUpdateDto(int? AttributeId, string? Value);

    /// <summary>
    /// One item bundled with an event setting. Quantity is PER PERSON — the
    /// session multiplies it by its headcount.
    /// </summary>
    public record SettingItemDto(
        int Id,
        int ItemId,
        string ItemName,
        decimal ItemPrice,
        string? CategoryName,
        decimal QuantityPerPerson
    );

    /// <summary>What the admin form posts back for each bundled item.</summary>
    public record SettingItemUpsertDto(int ItemId, decimal QuantityPerPerson);

    public record SettingDto(
        int Id, string Name, string Type, int GameId, string GameName,decimal Hours, decimal Price, DateTime CreatedOn, DateTime? ModifiedOn,
        string CreatedBy, string? ModifiedBy , bool IsOffer, bool IsOpenHour , bool IsDayPass,
        // Hidden / soft-deleted settings have IsActive=false. The UI uses this
        // to render a "Hidden" badge and to let admins restore via the toggle
        // in the edit modal.
        bool IsActive = true,
        // Event settings (Pre Release, Draft…) can hand out stock items.
        // Trailing with defaults so existing positional callers keep working.
        bool IsEvent = false,
        List<SettingItemDto>? Items = null
       );

    public record SettingCreateDto(string Name, string Type, int GameId, decimal Hours, decimal Price , bool IsOffer, bool IsOpenHour , bool IsDayPass,
        bool IsEvent = false, List<SettingItemUpsertDto>? Items = null);
    public record SettingUpdateDto(string? Name, string? Type, int? GameId, decimal Hours, decimal Price , bool IsOffer, bool IsOpenHour , bool IsDayPass, bool? IsActive = null,
        bool IsEvent = false, List<SettingItemUpsertDto>? Items = null);
}
