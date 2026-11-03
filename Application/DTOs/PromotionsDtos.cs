namespace Application.DTOs
{
    // ───────────────────────── Invoice adjustment (guarded) ─────────────────
    /// <summary>Result of the one permitted touch on transactions.TotalPrice.</summary>
    public record InvoiceAdjustResultDto(
        bool Success,
        string? Error,
        int TransactionId,
        decimal OldTotal,
        decimal NewTotal,
        decimal AppliedDelta,
        bool AlreadyApplied = false);

    // ───────────────────────── Gift cards ────────────────────────────────────
    public record GiftCardDto(
        int Id, string Code, decimal InitialAmount, decimal Balance, string CurrencyCode,
        DateTime IssuedOn, DateTime? ExpiresOn, string? IssuedTo, int? UserId,
        string Status, string? Notes, string CreatedBy, int? BranchId);

    public record GiftCardTransactionDto(
        int Id, int GiftCardId, string Type, decimal Amount, int? TransactionId,
        decimal BalanceAfter, DateTime On, string By, string? Note);

    public record GiftCardIssueDto(
        decimal Amount, string? CurrencyCode = null, DateTime? ExpiresOn = null,
        string? IssuedTo = null, int? UserId = null, string? Notes = null);

    public record GiftCardAdjustDto(decimal Delta, string? Note);
    public record GiftCardLoadDto(decimal Amount, string? Note);
    public record GiftCardRefundDto(decimal Amount, int? TransactionId, string? Note);

    /// <summary>Till lookup result — balance and whether the card can be used now.</summary>
    public record GiftCardLookupDto(
        string Code, decimal Balance, string CurrencyCode, string Status, bool CanRedeem,
        string? Reason, DateTime? ExpiresOn, string? IssuedTo);

    /// <summary>Redeem = apply part of the card balance AS PAYMENT on an open invoice.</summary>
    public record GiftCardRedeemDto(string Code, int TransactionId, decimal Amount);

    public record GiftCardRedeemResultDto(
        string Code, decimal AppliedAsPayment, decimal RemainingBalance,
        int TransactionId, decimal InvoiceTotalBefore, decimal InvoiceTotalAfter, string Status);

    public record GiftCardFilterDto(
        int Page = 1, int PageSize = 20, string? Search = null, string? Status = null);

    // ───────────────────────── Coupons ───────────────────────────────────────
    public record CouponDto(
        int Id, string Code, string Name, string Type, decimal Value,
        decimal? MinOrderAmount, decimal? MaxDiscountAmount, int? MaxUses, int? MaxUsesPerCustomer,
        int UsedCount, DateTime? ValidFrom, DateTime? ValidTo, string? CategoryIdsCsv,
        bool IsActive, string CreatedBy, DateTime CreatedOn, int? BranchId);

    public record CouponUpsertDto(
        string Code, string Name, string Type, decimal Value,
        decimal? MinOrderAmount = null, decimal? MaxDiscountAmount = null,
        int? MaxUses = null, int? MaxUsesPerCustomer = null,
        DateTime? ValidFrom = null, DateTime? ValidTo = null,
        string? CategoryIdsCsv = null, bool IsActive = true);

    public record CouponRedemptionDto(
        int Id, int CouponId, string CouponCode, int TransactionId, int? UserId,
        decimal DiscountAmount, DateTime RedeemedOn, string RedeemedBy);

    /// <summary>
    /// Validate without applying. Either pass TransactionId (the service reads
    /// the open invoice's total and category mix) or OrderAmount + CategoryIds
    /// for a what-if from the till before the order exists.
    /// </summary>
    public record CouponValidateDto(
        string Code, int? TransactionId = null, decimal? OrderAmount = null,
        List<int>? CategoryIds = null, int? UserId = null);

    public record CouponValidationResultDto(
        bool Eligible, string? Reason, string Code, string? Name, string? Type, decimal Value,
        decimal OrderAmount, decimal EligibleAmount, decimal DiscountAmount);

    public record CouponRedeemDto(string Code, int TransactionId);

    public record CouponRedeemResultDto(
        string Code, int TransactionId, decimal DiscountAmount,
        decimal InvoiceTotalBefore, decimal InvoiceTotalAfter, int RedemptionId);

    public record CouponFilterDto(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null);

    // ───────────────────────── Happy hour ────────────────────────────────────
    /// <summary>Times are "HH:mm" strings in venue local time (Asia/Beirut). DaysOfWeekCsv uses ISO 1=Mon … 7=Sun.</summary>
    public record HappyHourRuleDto(
        int Id, string Name, string DaysOfWeekCsv, string StartTime, string EndTime,
        decimal DiscountPercent, string? CategoryIdsCsv, string? ItemIdsCsv,
        bool IsActive, int Priority, int? BranchId, DateTime CreatedOn);

    public record HappyHourRuleUpsertDto(
        string Name, string DaysOfWeekCsv, string StartTime, string EndTime,
        decimal DiscountPercent, string? CategoryIdsCsv = null, string? ItemIdsCsv = null,
        bool IsActive = true, int Priority = 0);

    public record HappyHourActiveNowDto(
        DateTime ServerUtc, DateTime LocalTime, string TimeZoneId, List<HappyHourRuleDto> Rules);

    public record HappyHourPricePreviewDto(
        int ItemId, string ItemName, decimal BasePrice, decimal DiscountPercent,
        decimal DiscountedPrice, int? RuleId, string? RuleName, bool Active);

    public record HappyHourApplyDto(int TransactionId);

    public record HappyHourApplyResultDto(
        int TransactionId, int RuleId, string RuleName, decimal DiscountAmount,
        decimal InvoiceTotalBefore, decimal InvoiceTotalAfter, int MatchedLines, bool AlreadyApplied = false);

    // ───────────────────────── Combos ────────────────────────────────────────
    public record ComboItemDto(int Id, int ItemId, string ItemName, int Quantity, decimal UnitPrice);

    public record ComboDto(
        int Id, string Name, string? Description, decimal Price, bool IsActive, string? ImagePath,
        int? BranchId, DateTime CreatedOn, decimal ItemsTotal, decimal Saving, List<ComboItemDto> Items);

    public record ComboItemUpsertDto(int ItemId, int Quantity);

    public record ComboUpsertDto(
        string Name, string? Description, decimal Price, bool IsActive = true,
        string? ImagePath = null, List<ComboItemUpsertDto>? Items = null);

    /// <summary>TransactionId null = open a new invoice for this combo.</summary>
    public record ComboSellDto(int ComboId, int? TransactionId = null, int? UserId = null);

    public record ComboSellResultDto(
        int ComboId, string ComboName, int TransactionId, bool CreatedNewInvoice,
        decimal ItemsTotal, decimal ComboPrice, decimal AdjustmentApplied,
        decimal InvoiceTotalAfter, TransactionDto? Invoice);

    // ───────────────────────── Automatic apply (phase 2) ─────────────────────
    /// <summary>Happy-hour state on an invoice after an evaluation. Delta is the signed change booked by THIS call (0 = unchanged).</summary>
    public record AutoApplyHappyHourDto(int RuleId, string RuleName, decimal Amount, decimal Delta, int MatchedLines);

    /// <summary>Combo state on an invoice after an evaluation: Quantity whole sets, Amount total saving, Delta booked by THIS call.</summary>
    public record AutoApplyComboDto(int ComboId, string ComboName, int Quantity, decimal Amount, decimal Delta);

    public record AutoApplyRequestDto(int TransactionId);

    public record AutoApplyResultDto(
        int TransactionId,
        AutoApplyHappyHourDto? HappyHour,
        List<AutoApplyComboDto> Combos,
        decimal TotalBefore,
        decimal TotalAfter,
        List<string> Messages);

    public record AppliedCouponDto(int CouponId, string Code, string Name, decimal Amount);

    /// <summary>What is currently booked on an invoice (read-only view, no evaluation).</summary>
    public record AppliedPromotionsDto(
        int TransactionId,
        bool IsOpen,
        decimal InvoiceTotal,
        AutoApplyHappyHourDto? HappyHour,
        List<AutoApplyComboDto> Combos,
        List<AppliedCouponDto> Coupons,
        decimal TotalDiscount);
}
