using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Whish Collect payments against existing open invoices (till, self-order, kiosk, customer display).</summary>
    public interface IWhishInvoicePaymentService
    {
        /// <summary>
        /// Creates a collect request for the invoice's remaining amount.
        /// successUrl / cancelUrl are where Whish sends the buyer; they may contain the
        /// placeholder "{requestId}" which is replaced with the new request id.
        /// uiReturnUrl is remembered so the return endpoint can bounce the buyer to the UI.
        /// Success=false with a clear message when Whish is not configured, the invoice is
        /// not open, or nothing remains to pay.
        /// </summary>
        Task<BaseResponse<WhishStartResultDto>> StartAsync(int transactionId, string source, string successUrl, string cancelUrl, string? uiReturnUrl = null, CancellationToken ct = default);

        /// <summary>
        /// Re-asks Whish for the collect status while Pending; when paid records the tender
        /// once and closes the invoice when fully paid. Safe to call repeatedly.
        /// </summary>
        Task<BaseResponse<WhishStatusDto>> CheckAsync(int requestId, CancellationToken ct = default);

        /// <summary>UI url remembered at start (null when none was given).</summary>
        Task<string?> GetReturnUrlAsync(int requestId, CancellationToken ct = default);

        /// <summary>True when the guest token (self-order session GUID or kiosk device token) owns the invoice.</summary>
        Task<bool> ValidatePublicTokenAsync(string? token, int transactionId, CancellationToken ct = default);
    }
}
