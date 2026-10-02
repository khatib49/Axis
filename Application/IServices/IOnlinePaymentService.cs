using Application.DTOs;

namespace Application.IServices
{
    public interface IOnlinePaymentService
    {
        // ── Admin ────────────────────────────────────────────────────────
        Task<OnlinePaymentDto> CreateAsync(OnlinePaymentCreateDto dto, string actor, CancellationToken ct = default);
        Task<OnlinePaymentsPageDto> ListAsync(OnlinePaymentFilterDto filter, CancellationToken ct = default);
        Task<OnlinePaymentDetailDto?> GetAsync(int id, CancellationToken ct = default);
        /// <summary>Ask the gateway for the latest state and apply it (safety net for a missed callback).</summary>
        Task<OnlinePaymentDto?> ReconcileAsync(int id, string actor, CancellationToken ct = default);
        /// <summary>Admin cancels a link that should no longer be payable.</summary>
        Task<bool> CancelAsync(int id, string actor, string? reason, CancellationToken ct = default);
        /// <summary>Cancel a still-open pay link by its public code (no-op when already paid/cancelled/unknown).</summary>
        Task<bool> CancelOpenByCodeAsync(string code, string actor, string? reason, CancellationToken ct = default);
        Task<PaymentProviderConfigDto> GetProviderConfigAsync(string provider, CancellationToken ct = default);

        // ── Public (pay page) ────────────────────────────────────────────
        Task<PublicPaymentDto?> GetPublicAsync(string code, CancellationToken ct = default);
        Task<PublicPaymentStartResultDto> StartCheckoutAsync(string code, CancellationToken ct = default);
        /// <summary>Result-page poll: if still waiting, ask the gateway (throttled) and apply a final answer; returns the public view.</summary>
        Task<PublicPaymentDto?> CheckPublicAsync(string code, CancellationToken ct = default);

        // ── Gateway callback ─────────────────────────────────────────────
        /// <summary>Verifies, records and applies a gateway callback. Never throws; always returns quickly.</summary>
        Task HandleCallbackAsync(string provider, IReadOnlyDictionary<string, string> form, string raw, CancellationToken ct = default);

        // ── Used by other modules ────────────────────────────────────────
        /// <summary>Create a payment for a known reference and return its public pay URL + id (no gateway call yet).</summary>
        Task<(OnlinePaymentDto Payment, string PayUrl)> CreateForReferenceAsync(
            string purpose, string referenceType, int referenceId, decimal amount, string currency, string description,
            string? customerName, string? customerPhone, string? customerEmail, int? userId, string actor,
            CancellationToken ct = default);
        Task<bool> IsProviderReadyAsync(CancellationToken ct = default);
        string BuildPayUrl(string code, string publicBase);
    }
}
