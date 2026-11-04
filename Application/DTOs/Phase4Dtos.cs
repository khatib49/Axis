namespace Application.DTOs
{
    // ── 3D menu ─────────────────────────────────────────────────────────
    public record ItemModel3DDto(
        int Id, int ItemId, string ItemName, string GlbPath, string? UsdzPath, string? PosterPath,
        long GlbBytes, bool IsActive, DateTime? UpdatedOn, string UpdatedBy);
    /// <summary>What public menus need: paths are relative to the API host (same as item images).</summary>
    public record PublicItemModelDto(int ItemId, string GlbPath, string? UsdzPath, string? PosterPath);

    // ── Upsells ─────────────────────────────────────────────────────────
    public record UpsellSuggestRequest(List<int> ItemIds, string? Channel = null, int? Limit = null);
    public record UpsellSuggestionDto(
        int ItemId, string Name, decimal Price, string? ImagePath, string? CategoryName,
        string Reason, string? Message, string Source);
    public record UpsellRuleDto(
        int Id, int? TriggerItemId, string? TriggerItemName, int? TriggerCategoryId, string? TriggerCategoryName,
        int SuggestItemId, string SuggestItemName, string? Message, int Priority, bool IsActive);
    public record UpsellRuleUpsertDto(int? TriggerItemId, int? TriggerCategoryId, int SuggestItemId, string? Message, int Priority = 0, bool IsActive = true);
    public record UpsellPairDto(int ItemId, string ItemName, int SuggestItemId, string SuggestItemName, int PairCount, decimal Confidence, decimal Lift, string? Pitch, DateTime ComputedOn);
    public record UpsellRebuildResultDto(int InvoicesScanned, int PairsKept, int PitchesWritten, DateTime ComputedOn, string? AiNote);
    public record UpsellEventDto(string Channel, string Action, int SuggestItemId, string? Source = null);
    public record UpsellItemStatDto(int SuggestItemId, string Name, int Shown, int Accepted, decimal AcceptRate, decimal AcceptedRevenue);
    public record UpsellChannelStatDto(string Channel, int Shown, int Accepted, decimal AcceptRate, decimal AcceptedRevenue);
    public record UpsellStatsDto(int Days, int Shown, int Accepted, decimal AcceptRate, decimal AcceptedRevenue,
        List<UpsellChannelStatDto> ByChannel, List<UpsellItemStatDto> ByItem);

    // ── Delivery apps ───────────────────────────────────────────────────
    public record DeliveryPlatformDto(
        int Id, string Source, string DisplayName, bool IsActive, string? StatusCallbackUrl,
        decimal PriceMarkupPercent, bool AutoAccept, bool MappedItemsOnly, bool SecretConfigured,
        string WebhookPath, string MenuFeedPath, int MappedItems, int PendingPushes, int FailedPushes);
    public record DeliveryPlatformUpsertDto(
        string Source, string DisplayName, bool IsActive = true, string? StatusCallbackUrl = null,
        decimal PriceMarkupPercent = 0, bool AutoAccept = false, bool MappedItemsOnly = false);
    public record DeliveryItemMappingDto(int Id, int PlatformId, string ExternalItemId, int ItemId, string ItemName, decimal ItemPrice, decimal? PriceOverride, bool IsAvailable);
    public record DeliveryItemMappingUpsertDto(string ExternalItemId, int ItemId, decimal? PriceOverride = null, bool IsAvailable = true);
    public record DeliveryMenuFeedItemDto(string ExternalId, int ItemId, string Name, decimal Price, bool Available, string? ImageUrl);
    public record DeliveryMenuFeedCategoryDto(int Id, string Name, List<DeliveryMenuFeedItemDto> Items);
    public record DeliveryMenuFeedDto(string Source, string Currency, DateTime GeneratedOn, List<DeliveryMenuFeedCategoryDto> Categories);
    public record DeliveryPlatformPushDto(
        int Id, int DeliveryOrderId, string ExternalRef, string Status, string PlatformStatus, string State,
        int Attempts, int? LastHttpStatus, string? LastError, DateTime CreatedOn, DateTime? SentOn, DateTime? NextAttemptOn);
    public record DeliveryPushRunResultDto(int Enqueued, int Sent, int Failed, int Retrying);
    public record DeliveryCallbackTestDto(bool Success, int? HttpStatus, string Message);

    // ── Card terminals ──────────────────────────────────────────────────
    public record CardTerminalDto(int Id, string Name, string Provider, string? ProviderTerminalId, int? BranchId, bool IsActive, bool Busy);
    public record CardTerminalUpsertDto(string Name, string Provider, string? ProviderTerminalId = null, int? BranchId = null, bool IsActive = true);
    public record TerminalStartDto(int TransactionId, int TerminalId, decimal? Amount = null);
    public record TerminalManualConfirmDto(string ApprovalCode, string? CardLast4 = null, string? CardScheme = null);
    public record TerminalPaymentDto(
        int Id, int TransactionId, int TerminalId, string TerminalName, string Provider, decimal Amount, string Currency,
        string ExternalId, string Status, string? ApprovalCode, string? CardLast4, string? CardScheme, string? Rrn,
        string? Message, DateTime CreatedOn, string CreatedBy, DateTime? CompletedOn, bool InvoiceClosed);
    public record TerminalCallbackDto(string Reference, string Status, string? ApprovalCode = null, string? Last4 = null, string? Scheme = null, string? Rrn = null, string? Message = null);
    public record TerminalSchemeTotalDto(string Scheme, int Count, decimal Amount);
    public record TerminalTotalDto(int TerminalId, string TerminalName, int Approved, decimal ApprovedAmount, int Declined);
    public record TerminalReportDto(DateTime From, DateTime To, int Approved, decimal ApprovedAmount, int Declined, int Cancelled,
        List<TerminalTotalDto> ByTerminal, List<TerminalSchemeTotalDto> ByScheme, List<TerminalPaymentDto> Payments);
}
