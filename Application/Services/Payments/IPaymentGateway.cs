namespace Application.Services.Payments
{
    /// <summary>
    /// Result of starting a hosted payment. RedirectUrl is where the browser
    /// must be sent; Reference is what the provider will echo back so we can
    /// match the callback to the right registration row.
    /// </summary>
    public record PaymentStartResult(bool Success, string? RedirectUrl, string? Reference, string? Error);

    /// <summary>
    /// Common shape for the two hosted checkouts we support (Stripe for
    /// cards, Whish Collect for the Lebanese e-wallet). Keeping them behind
    /// one interface means the registration service doesn't care which one
    /// is in play, and a third provider is a new class + one switch arm.
    /// </summary>
    public interface IPaymentGateway
    {
        /// <summary>Provider key: "Visa" (Stripe) or "Whish".</summary>
        string Method { get; }

        /// <summary>True when the required credentials are configured.</summary>
        Task<bool> IsConfiguredAsync(CancellationToken ct = default);

        Task<PaymentStartResult> StartAsync(
            int registrationId,
            decimal amount,
            string currency,
            string description,
            string customerName,
            string? customerEmail,
            string successUrl,
            string cancelUrl,
            CancellationToken ct = default);
    }
}
