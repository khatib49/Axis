namespace Application.DTOs
{
    // ── Segments ─────────────────────────────────────────────────────────
    /// <summary>All rules optional; the ones present are AND-ed.</summary>
    public record SegmentRulesDto(
        int? LastVisitWithinDays = null,
        int? LastVisitOlderThanDays = null,
        decimal? MinSpend = null,
        int? SpendPeriodDays = null,
        int? MinVisits = null,
        int? LoyaltyTicketsMin = null,
        bool? HasEmail = null,
        bool? HasPhone = null,
        int? BranchId = null);

    public record SegmentDto(int Id, string Name, SegmentRulesDto Rules, bool IsActive, DateTime CreatedOn, string CreatedBy);
    public record SegmentCreateDto(string Name, SegmentRulesDto? Rules, bool IsActive = true);
    public record SegmentUpdateDto(string? Name, SegmentRulesDto? Rules, bool? IsActive);

    public record AudienceRowDto(int UserId, string Name, string? Phone, string? Email, DateTime? LastVisit, decimal Spend);
    public record AudiencePreviewDto(int Count, IReadOnlyList<AudienceRowDto> Rows);

    // ── Campaigns ────────────────────────────────────────────────────────
    public record CampaignDto(int Id, string Name, string Channel, int SegmentId, string? SegmentName, string? Subject, string Body,
        string? WhatsAppTemplate, string? PromoCode, string Status, DateTime? ScheduledFor, DateTime? SentOn,
        int AudienceCount, int SentCount, int FailedCount, DateTime CreatedOn, string CreatedBy, int? BranchId);

    public record CampaignCreateDto(string Name, string Channel, int SegmentId, string Body, string? Subject = null,
        string? WhatsAppTemplate = null, string? PromoCode = null, DateTime? ScheduledFor = null, int? BranchId = null);

    public record CampaignUpdateDto(string? Name = null, string? Channel = null, int? SegmentId = null, string? Body = null,
        string? Subject = null, string? WhatsAppTemplate = null, string? PromoCode = null, DateTime? ScheduledFor = null, int? BranchId = null);

    public record CampaignScheduleDto(DateTime ScheduledFor);
    public record CampaignTestSendDto(string Address);
    public record CampaignTestSendResultDto(string Address, string Status, string? Error);

    public record CampaignRecipientDto(int Id, int CampaignId, int? UserId, string Address, string Status, string? Error, DateTime? SentOn, bool IsTest);
    public record CampaignStatsDto(int CampaignId, string Status, int AudienceCount, int Pending, int Sent, int Failed, int Skipped, DateTime? SentOn);
    public record CampaignSendSummaryDto(int CampaignId, string Status, int AudienceCount, int Sent, int Failed, int Skipped);

    // ── Unsubscribes ─────────────────────────────────────────────────────
    public record UnsubscribeDto(int Id, string Address, string Channel, DateTime On, string Source);
    public record UnsubscribeCreateDto(string Address);
}
