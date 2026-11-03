namespace Application.IServices
{
    /// <summary>Sent = accepted by the gateway; Configured = false when Sms.* settings are missing.</summary>
    public record SmsSendResult(bool Sent, bool Configured, string? Error);

    public interface ISmsSender
    {
        Task<SmsSendResult> SendAsync(string to, string text, CancellationToken ct = default);
    }
}
