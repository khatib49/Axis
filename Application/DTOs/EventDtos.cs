namespace Application.DTOs
{
    /// <summary>One feature card on the public page.</summary>
    public record EventFeatureDto(string Icon, string Title, string Desc);

    /// <summary>Full admin view of an event.</summary>
    public record EventDto(
        int Id,
        string Key,
        string Title,
        string? Subtitle,
        string? Description,
        DateTime? EventDate,
        string? Location,
        List<EventFeatureDto> Features,
        string? VideoPath,
        string? VideoYoutubeId,
        string? HeroImagePath,
        decimal Price,
        string Currency,
        bool EnableVisa,
        bool EnableWhish,
        bool EnableCash,
        string? WhishPaymentLink,
        string? WhatsAppNumber,
        string? WhatsAppTemplate,
        bool IsPublished,
        bool IsActive,
        int? Capacity,
        DateTime CreatedOn,
        // Live counters so the admin list can show progress at a glance.
        int RegistrationCount,
        int PaidCount
    );

    public record EventUpsertDto(
        string Key,
        string Title,
        string? Subtitle,
        string? Description,
        DateTime? EventDate,
        string? Location,
        List<EventFeatureDto>? Features,
        string? VideoYoutubeId,
        decimal Price,
        string Currency,
        bool EnableVisa,
        bool EnableWhish,
        bool EnableCash,
        string? WhishPaymentLink,
        string? WhatsAppNumber,
        string? WhatsAppTemplate,
        bool IsPublished,
        bool IsActive,
        int? Capacity
    );

    /// <summary>
    /// Everything the anonymous landing page needs. Payment flags already
    /// account for BOTH the per-event toggle AND whether the gateway has
    /// credentials configured — the page just renders what it's given.
    /// </summary>
    public record EventPublicDto(
        string Key,
        string Title,
        string? Subtitle,
        string? Description,
        DateTime? EventDate,
        string? Location,
        List<EventFeatureDto> Features,
        string? VideoUrl,          // resolved absolute/relative URL of the uploaded file
        string? VideoYoutubeId,
        string? HeroImageUrl,
        decimal Price,
        string Currency,
        bool VisaAvailable,
        bool WhishAvailable,
        bool CashAvailable,
        bool IsSoldOut
    );

    public record MediaUploadResultDto(string Path, string Url);
}
