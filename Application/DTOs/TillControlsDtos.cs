namespace Application.DTOs
{
    // ───────────────────────── Shifts ─────────────────────────
    public record OpenShiftDto(string RegisterName, decimal OpeningFloat, string? Notes);
    public record CashMovementCreateDto(decimal Amount, string Reason);
    public record CloseShiftDto(decimal CountedCash, string? Notes);
    public record ShiftFilterDto(
        int? UserId = null, string? RegisterName = null, string? Status = null,
        DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 20);

    public record CashMovementDto(int Id, int ShiftId, string Type, decimal Amount, string Reason, DateTime CreatedOn, string CreatedBy);

    public record CashierShiftDto(
        int Id, string RegisterName, int UserId, string UserName,
        DateTime OpenedOn, DateTime? ClosedOn, decimal OpeningFloat,
        decimal? ExpectedCash, decimal? CountedCash, decimal? Variance,
        string Status, string? Notes, string? ClosedBy, int? BranchId,
        List<CashMovementDto> Movements);

    public record ShiftMethodTotalDto(string Method, decimal AmountInBase, int Count);
    public record ShiftCurrencyTotalDto(string CurrencyCode, decimal AmountInCurrency, decimal AmountInBase, int Count);

    /// <summary>X report (live, shift still open) or Z report (frozen at close, Variance filled).</summary>
    public record ShiftReportDto(
        string ReportType, // "X" | "Z"
        int ShiftId, string RegisterName, string UserName,
        DateTime OpenedOn, DateTime? ClosedOn, DateTime GeneratedOn,
        decimal OpeningFloat,
        List<ShiftMethodTotalDto> SalesByMethod,
        List<ShiftCurrencyTotalDto> TendersByCurrency,
        decimal TenderedTotalInBase,
        decimal CashTenderedInBase,
        int TransactionCount, decimal TransactionTotal,
        int PayInCount, decimal PayInTotal,
        int PayOutCount, decimal PayOutTotal,
        int VoidsPending, int VoidsApproved, decimal VoidsApprovedAmount,
        int RefundsApproved, decimal RefundsApprovedAmount,
        decimal ExpectedCash,
        decimal? CountedCash, decimal? Variance);

    // ───────────────────────── Voids ─────────────────────────
    public record VoidReasonDto(int Id, string Name, bool RequiresApproval, bool IsActive);
    public record VoidReasonCreateDto(string Name, bool RequiresApproval = true, bool IsActive = true);
    public record VoidReasonUpdateDto(string? Name, bool? RequiresApproval, bool? IsActive);

    public record VoidRequestCreateDto(int TransactionId, string Type, decimal? Amount, int ReasonId, string? ReasonText);
    public record VoidDecisionDto(string? Note);
    public record VoidRequestFilterDto(string? Status = null, int? TransactionId = null, int Page = 1, int PageSize = 20);
    public record TillVoidRequestDto(
        int Id, int TransactionId, string Type, decimal? Amount,
        int ReasonId, string ReasonName, bool ReasonRequiresApproval, string? ReasonText,
        string RequestedBy, DateTime RequestedOn, string Status,
        string? DecidedBy, DateTime? DecidedOn, string? DecisionNote, int? BranchId);

    // ───────────────────────── Currency ─────────────────────────
    public record CurrencyDto(string Code, string Name, string Symbol, int DecimalPlaces, bool IsBase, bool IsActive, decimal RoundingStep, decimal? CurrentRate);
    public record CurrencyCreateDto(string Code, string Name, string Symbol, int DecimalPlaces = 2, bool IsBase = false, bool IsActive = true, decimal RoundingStep = 0.01m, decimal? InitialRate = null);
    public record CurrencyUpdateDto(string? Name, string? Symbol, int? DecimalPlaces, bool? IsActive, decimal? RoundingStep);
    public record SetRateDto(string CurrencyCode, decimal Rate, DateTime? EffectiveFrom = null);
    public record ExchangeRateDto(int Id, string CurrencyCode, decimal Rate, DateTime EffectiveFrom, DateTime CreatedOn, string CreatedBy);
    public record ConversionResultDto(decimal Amount, string From, string To, decimal Result, decimal RateFrom, decimal RateTo, decimal RoundingStep);

    // ───────────────────────── Tenders ─────────────────────────
    public record TenderLineDto(string CurrencyCode, decimal Amount, string Method = "Cash", string? Reference = null);
    public record AddTendersDto(int TransactionId, List<TenderLineDto> Tenders);
    public record PaymentTenderDto(
        int Id, int TransactionId, int? ShiftId, string CurrencyCode, decimal AmountInCurrency,
        decimal RateUsed, decimal AmountInBase, string Method, string? Reference, DateTime CreatedOn, string CreatedBy);
    public record ChangeDueDto(string CurrencyCode, decimal Amount);
    public record TenderSummaryDto(
        int TransactionId, string BaseCurrency, decimal TotalPrice, decimal PaidInBase,
        decimal RemainingInBase, decimal ChangeInBase, List<ChangeDueDto> ChangeByCurrency,
        List<PaymentTenderDto> Tenders);
}
