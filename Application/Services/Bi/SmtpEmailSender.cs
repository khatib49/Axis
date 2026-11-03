using System.Net;
using System.Net.Mail;
using System.Text;
using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Bi
{
    /// <summary>
    /// System.Net.Mail over the Smtp.* IntegrationSettings keys
    /// (Smtp.Host, Smtp.Port, Smtp.User, Smtp.Password, Smtp.From). No new packages.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IIntegrationSettingsService _settings;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IIntegrationSettingsService settings, ILogger<SmtpEmailSender> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<EmailSendResult> SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml,
            EmailAttachment? attachment = null, CancellationToken ct = default)
        {
            var recipients = to.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
            if (recipients.Count == 0) return new(false, true, "No recipients.");

            var host = await _settings.GetRawAsync("Smtp.Host", ct);
            var from = await _settings.GetRawAsync("Smtp.From", ct);
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            {
                _logger.LogWarning("SMTP not configured (Smtp.Host / Smtp.From missing); e-mail '{Subject}' not sent", subject);
                return new(false, false, "SMTP not configured");
            }
            var portRaw = await _settings.GetRawAsync("Smtp.Port", ct);
            var port = int.TryParse(portRaw, out var p) && p > 0 ? p : 587;
            var user = await _settings.GetRawAsync("Smtp.User", ct);
            var password = await _settings.GetRawAsync("Smtp.Password", ct);

            try
            {
                using var client = new SmtpClient(host.Trim(), port)
                {
                    EnableSsl = port != 25,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                };
                if (!string.IsNullOrWhiteSpace(user))
                    client.Credentials = new NetworkCredential(user.Trim(), password ?? "");

                using var msg = new MailMessage
                {
                    From = new MailAddress(from.Trim()),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8,
                };
                foreach (var r in recipients) msg.To.Add(r);
                if (attachment is not null)
                {
                    var stream = new MemoryStream(Encoding.UTF8.GetBytes(attachment.Content));
                    msg.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.MediaType));
                }
                await client.SendMailAsync(msg, ct);
                _logger.LogInformation("E-mail '{Subject}' sent to {Count} recipient(s)", subject, recipients.Count);
                return new(true, true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send e-mail '{Subject}'", subject);
                return new(false, true, ex.Message);
            }
        }
    }
}
