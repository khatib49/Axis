namespace Application.DTOs
{
    /// <summary>One feature card on the public page.</summary>
    public record EventFeatureDto(string Icon, string Title, string Desc);

    /// <summary>
    /// A ticket type as the admin edits it. Key is stable (generated on first
    /// save) so registrations keep pointing at it through renames.
    /// </summary>
    public record EventTicketTypeDto(
        string? Key,
        string Name,
        decimal Price,
        string? Description = null,
        int? Capacity = null,
        bool IsActive = true);

    /// <summary>Admin view of a ticket type with live counts.</summary>
    public record EventTicketTypeStatsDto(
        string Key, string Name, decimal Price, string? Description, int? Capacity, bool IsActive,
        int PaidCount, int PendingCount);

    /// <summary>What a visitor can pick on the public page (active types only).</summary>
    public record EventPublicTicketTypeDto(
        string Key, string Name, decimal Price, string? Description,
        bool IsSoldOut,
        /// Seats left when the type has a capacity; null = unlimited.
        int? Remaining);

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
        int PaidCount,
        // Calendar chip color. Trailing default keeps positional callers valid.
        string Type = "Other",
        List<EventTicketTypeStatsDto>? TicketTypes = null
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
        int? Capacity,
        string? Type = null,
        /// Null = leave the event's ticket types as they are; [] = single price.
        List<EventTicketTypeDto>? TicketTypes = null
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
        bool IsSoldOut,
        /// Empty = single ticket at Price.
        List<EventPublicTicketTypeDto>? TicketTypes = null
    );

    /// <summary>
    /// Card-sized view of a published event for the public website listing
    /// (/events). No payment or registration internals are exposed.
    /// </summary>
    public record EventPublicSummaryDto(
        string Key,
        string Title,
        string? Subtitle,
        DateTime? EventDate,
        string? Location,
        string Type,
        decimal Price,
        string Currency,
        string? HeroImageUrl,
        int? Capacity,
        bool IsSoldOut,
        /// Highest active ticket-type price; above Price means "From $Price".
        decimal? PriceMax = null
    );

    public record MediaUploadResultDto(string Path, string Url);
}
