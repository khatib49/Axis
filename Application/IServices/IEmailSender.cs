namespace Application.IServices
{
    public record EmailAttachment(string FileName, string Content, string MediaType = "text/csv");

    /// <summary>Sent = delivered to SMTP; Configured = false when Smtp.* settings are missing.</summary>
    public record EmailSendResult(bool Sent, bool Configured, string? Error);

    public interface IEmailSender
    {
        Task<EmailSendResult> SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml,
            EmailAttachment? attachment = null, CancellationToken ct = default);
    }
}
