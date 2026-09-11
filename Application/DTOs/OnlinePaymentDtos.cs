namespace Application.DTOs
{
    // ── Admin ────────────────────────────────────────────────────────────

    /// <summary>Admin creates a free-form pay link ("Custom") or a link for a known reference.</summary>
    public record OnlinePaymentCreateDto(
        decimal Amount,
        string Description,
        string? Currency = "USD",
        string? CustomerName = null,
        string? CustomerPhone = null,
        string? CustomerEmail = null,
        int? UserId = null,
        /// "Custom" (default) | "WalletTopUp" | "Invoice" | "EventTicket"
        string? Purpose = null,
        string? ReferenceType = null,
        int? ReferenceId = null,
        /// Hours until the link stops working (null = never).
        int? ExpiresInHours = null,
        string? Provider = null);

    public record OnlinePaymentDto(
        int Id, string Code, string Provider, string Environment, string Purpose,
        string? ReferenceType, int? ReferenceId, string? ReferenceLabel,
        decimal Amount, string Currency, string Description,
        string? CustomerName, string? CustomerPhone, string? CustomerEmail, int? UserId,
        string Status, string? ProviderOrderNumber, string? ProviderPaymentId, string? ProviderStatus,
        string? PaymentMethodUsed, string? CardMasked, string? FailureReason,
        bool IsFulfilled, DateTime? FulfilledOn, string? FulfillmentError,
        int CallbackCount, DateTime? LastCallbackOn,
        string? CreatedBy, DateTime CreatedOn, DateTime? PaidOn, DateTime? ExpiresOn,
        string PayUrl);

    public record OnlinePaymentEventDto(
        int Id, string Kind, string? ProviderType, string? ProviderStatus, string? OrderStatus,
        string? ResultStatus, string? Note, bool HashValid, DateTime CreatedOn, string? Raw);

    public record OnlinePaymentDetailDto(OnlinePaymentDto Payment, List<OnlinePaymentEventDto> Events);

    public record OnlinePaymentFilterDto(
        DateTime? From = null, DateTime? To = null,
        string? Status = null, string? Purpose = null, string? Provider = null, string? Environment = null,
        string? Search = null, int Page = 1, int PageSize = 50);

    public record OnlinePaymentSummaryDto(
        decimal PaidAmount, int PaidCount,
        decimal PendingAmount, int PendingCount,
        decimal FailedAmount, int FailedCount,
        decimal RefundedAmount, int RefundedCount,
        List<OnlinePaymentBucketDto> ByPurpose,
        List<OnlinePaymentBucketDto> ByProvider);

    public record OnlinePaymentBucketDto(string Key, decimal PaidAmount, int PaidCount);

    public record OnlinePaymentsPageDto(
        OnlinePaymentSummaryDto Summary, int TotalCount, List<OnlinePaymentDto> Rows, int Page, int PageSize);

    /// <summary>What the admin needs to hand to the gateway + what is configured.</summary>
    public record PaymentProviderConfigDto(
        string Provider, string Environment, bool SandboxConfigured, bool ProductionConfigured,
        bool ActiveConfigured, string CallbackUrl, string SuccessUrlSample, string CancelUrlSample,
        string PublicBaseUrl, string HashAlgorithm);

    // ── Public (pay page) ────────────────────────────────────────────────

    public record PublicPaymentDto(
        string Code, string Provider, string Purpose, decimal Amount, string Currency, string Description,
        string? CustomerName, string Status, bool CanPay, bool IsExpired, DateTime? PaidOn,
        string? ReferenceLabel);

    public record PublicPaymentStartResultDto(bool Success, string? RedirectUrl, string Status, string? Error);
}
