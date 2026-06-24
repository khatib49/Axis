namespace Application.IServices
{
    /// <summary>
    /// Wraps the Meta WhatsApp Cloud API. Sends one template message per
    /// recipient and writes a WhatsAppMessages row per send (success or fail).
    /// </summary>
    public interface IWhatsAppService
    {
        Task<WhatsAppBlastResult> SendBlastAsync(
            int? pendingActionId,
            IEnumerable<WhatsAppRecipient> recipients,
            string templateName,
            IEnumerable<string> templateParams,
            CancellationToken ct = default);
    }

    public record WhatsAppRecipient(string Phone, string? Name, IEnumerable<string>? PerRecipientParams = null);

    public record WhatsAppBlastResult(int Sent, int Failed, IReadOnlyList<string> Errors);
}
