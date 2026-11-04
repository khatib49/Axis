namespace Application.DTOs
{
    // ── Tournaments ─────────────────────────────────────────────────────
    public record TournamentUpsertDto(string Name, string GameTitle, DateTime StartsOn, int MaxPlayers, decimal EntryFee, decimal PrizePool, string? PrizeSplit, string? Rules, string? RoomIds);
    public record TournamentParticipantDto(int Id, string DisplayName, string? Phone, int? UserId, bool Paid, string? PaidVia, int? Seed, int? FinalPlace, decimal PrizeAmount);
    public record TournamentMatchDto(int Id, int Round, int Slot, int? Player1Id, string? Player1, int? Player2Id, string? Player2, int? WinnerId, string? Score, int? SetId, string? Station, string Status);
    public record TournamentDto(int Id, string Name, string GameTitle, string? Rules, DateTime StartsOn, int MaxPlayers, decimal EntryFee, decimal PrizePool, string PrizeSplit,
        string? RoomIds, string Status, string PublicToken, bool PrizesPaid, int Rounds, List<TournamentParticipantDto> Participants, List<TournamentMatchDto> Matches);
    public record TournamentSummaryDto(int Id, string Name, string GameTitle, DateTime StartsOn, string Status, int Players, int MaxPlayers, decimal EntryFee, decimal PrizePool, string PublicToken);
    public record TournamentRegisterDto(string DisplayName, string? Phone);
    public record TournamentAddPlayerDto(string DisplayName, string? Phone, int? UserId, bool Paid, string? PaidVia);
    public record TournamentResultDto(int WinnerId, string? Score);

    // ── Booking ─────────────────────────────────────────────────────────
    public record BookableStationDto(int SetId, string Station, int RoomId, string Room);
    public record BookingSlotDto(DateTime StartsOn, DateTime EndsOn, bool Free);
    public record BookingCreateDto(int SetId, DateTime StartsOn, int DurationMinutes, string CustomerName, string Phone, int Players = 1, string? Notes = null);
    public record BookingDto(int Id, int SetId, string Station, string Room, DateTime StartsOn, DateTime EndsOn, string CustomerName, string Phone, int Players,
        string? Notes, string Status, string Token, string Source, DateTime CreatedOn, DateTime? CheckedInOn);
    public record BookingPublicDto(string Station, string Room, DateTime StartsOn, DateTime EndsOn, string CustomerName, int Players, string Status, string Token, bool CanCheckIn, bool CanCancel);
    public record BookingSettingsDto(int OpenHour, int CloseHour, int SlotMinutes, int MaxDays, bool RequireApproval, string TimeZone);

    // ── Station ordering ────────────────────────────────────────────────
    public record StationCodeDto(int SetId, string Station, string Room, string Token, bool IsActive);
    public record StationMenuItemDto(int Id, string Name, decimal Price, string? ImagePath, string Category);
    public record StationInfoDto(string Station, string Room, bool SessionRunning, string? Game, DateTime? StartedOn, DateTime? EndsOn, int? OpenInvoiceId, decimal OpenInvoiceTotal, List<StationMenuItemDto> Menu);
    public record StationOrderLine(int ItemId, int Quantity);
    public record StationOrderDto(List<StationOrderLine> Items, string? Note);
    public record StationOrderResultDto(int InvoiceId, decimal InvoiceTotal, string Message);

    // ── Owner bot ───────────────────────────────────────────────────────
    public record OwnerBotChatDto(int Id, string Channel, string ChatId, string? DisplayName, bool IsAllowed, DateTime FirstSeenOn, DateTime? LastMessageOn);
    public record OwnerBotStatusDto(bool TelegramConfigured, bool WhatsAppConfigured, bool AiConfigured, string TelegramWebhookPath, string WhatsAppWebhookPath, int DailySummaryHour, List<OwnerBotChatDto> Chats);

    // ── Parent wallet ───────────────────────────────────────────────────
    public record ParentLinkUpsertDto(int ChildUserId, string ParentName, string? ParentPhone, string? ParentTelegramChatId, string? ParentEmail, decimal? DailyLimit, decimal? LowBalanceThreshold, bool NotifyOnSpend = true, bool NotifyOnSessionEnd = true, bool IsActive = true);
    public record ParentLinkDto(int Id, int ChildUserId, string ChildName, string ParentName, string? ParentPhone, string? ParentTelegramChatId, string? ParentEmail,
        decimal? DailyLimit, decimal? LowBalanceThreshold, bool NotifyOnSpend, bool NotifyOnSessionEnd, bool IsActive, string Token, decimal Balance, decimal SpentToday);
    public record ParentViewTxDto(DateTime On, string Type, decimal Amount, decimal BalanceAfter, string? Notes);
    public record ParentViewDto(string ChildName, decimal Balance, decimal SpentToday, decimal? DailyLimit, decimal? RemainingToday, List<ParentViewTxDto> Recent);

    // ── Alerts ──────────────────────────────────────────────────────────
    public record AlertRuleUpsertDto(string Name, string Metric, string Comparator, decimal Threshold, int WindowMinutes, int FromHour, int ToHour, string Channels, string? Recipients, int CooldownMinutes, bool IsActive = true);
    public record AlertRuleDto(int Id, string Name, string Metric, string Comparator, decimal Threshold, int WindowMinutes, int FromHour, int ToHour, string Channels, string? Recipients, int CooldownMinutes, bool IsActive, DateTime? LastFiredOn);
    public record AlertCheckDto(int RuleId, decimal Value, bool Triggered, string Message);
    public record AlertEventDto(int Id, int RuleId, string RuleName, decimal Value, string Message, string? Delivery, DateTime FiredOn);
    public record AlertRunResultDto(int Checked, int Fired, List<AlertCheckDto> Results);

    // ── AI tools ────────────────────────────────────────────────────────
    public record ForecastDayDto(DateTime Date, decimal Quantity);
    public record ForecastItemDto(int ItemId, string Name, decimal Total, List<ForecastDayDto> Days);
    public record ForecastIngredientDto(int IngredientId, string Name, string Unit, decimal Needed, decimal OnHand, decimal? ReorderLevel, decimal SuggestedPurchase);
    public record ForecastDto(int Days, int WeeksOfHistory, List<ForecastItemDto> Items, List<ForecastIngredientDto> Ingredients);
    public record AiDraftDto(int Id, string Kind, string FileName, string Json, string Status, string? Result, DateTime CreatedOn, string CreatedBy);
    public record MenuApplyDto(int DefaultQuantity = 100);
    public record InvoiceApplyLineDto(int IngredientId, decimal Quantity, decimal UnitCost, string? Notes);
    public record InvoiceApplyDto(int? SupplierId, string? NewSupplierName, DateTime? PurchaseDate, string? InvoiceNumber, List<InvoiceApplyLineDto> Lines);
    public record AiApplyResultDto(int DraftId, string Message, int Created, int Skipped, int? PurchaseId);

    // ── Setup ───────────────────────────────────────────────────────────
    public record SetupStepDto(string Key, string Title, bool Done, string Detail, string Link);
    public record SetupStatusDto(int Done, int Total, List<SetupStepDto> Steps, string? BusinessName, string Language);
    public record SetupBusinessDto(string Name, string? Phone, string? Address, string? Language, string? TimeZone);
}
