using Domain.Entities;

namespace Application.Services.Payments
{
    /// <summary>Result of creating a hosted checkout session for an OnlinePayment.</summary>
    public record CheckoutSessionResult(bool Success, string? RedirectUrl, string? OrderNumber, string? Error, string? Raw = null);

    /// <summary>
    /// Normalised view of a gateway callback / status response.
    /// <c>Outcome</c> is what the OnlinePayment should become:
    ///   "Paid" | "Failed" | "Cancelled" | "Refunded" | "Voided" | "Chargeback" | "Pending" | null (intermediary, no change)
    /// </summary>
    public record ProviderCallbackResult(
        bool HashValid,
        string? OrderNumber,
        string? ProviderPaymentId,
        string? ProviderType,
        string? ProviderStatus,
        string? OrderStatus,
        string? Outcome,
        string? Reason,
        string? PaymentMethod,
        string? CardMasked,
        decimal? Amount,
        string? Currency,
        string Raw);

    /// <summary>
    /// A hosted-checkout gateway the Online Payments module can drive.
    /// Adding a gateway = one class implementing this + one DI line; the
    /// ledger, admin page, pay page, callbacks and fulfilment are shared.
    /// </summary>
    public interface IOnlinePaymentProvider
    {
        /// <summary>Stable key stored on OnlinePayment.Provider, e.g. "MontyPay".</summary>
        string Key { get; }

        /// <summary>"sandbox" | "production" — whichever the admin switched on.</summary>
        Task<string> GetEnvironmentAsync(CancellationToken ct = default);

        Task<bool> IsConfiguredAsync(string? environment = null, CancellationToken ct = default);

        Task<CheckoutSessionResult> CreateCheckoutAsync(
            OnlinePayment payment, string successUrl, string cancelUrl, string notificationUrl,
            CancellationToken ct = default);

        /// <summary>Verify + normalise a callback body (form-urlencoded key/values).</summary>
        Task<ProviderCallbackResult> ParseCallbackAsync(IReadOnlyDictionary<string, string> form, string raw, CancellationToken ct = default);

        /// <summary>Ask the gateway for the current state (reconciliation). Null when unsupported / unreachable.</summary>
        Task<ProviderCallbackResult?> QueryStatusAsync(OnlinePayment payment, CancellationToken ct = default);
    }
}
