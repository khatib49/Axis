using System.Net;
using System.Text;
using Application.DTOs;
using Application.IServices;
using Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    /// <summary>
    /// Password reset by email using Identity's reset tokens. A token is
    /// single-use (it's tied to the security stamp, which changes with the
    /// password) and expires after Identity's token lifespan (IdentitySetup).
    /// The link goes to the dashboard's /reset-password page; its origin comes
    /// from setting Auth.PasswordResetBaseUrl, never from the request, so a
    /// caller can't make us email a link to their own site.
    /// </summary>
    public class PasswordResetService : IPasswordResetService
    {
        public const string DefaultBaseUrl = "https://www.axislb.com";

        private readonly UserManager<AppUser> _users;
        private readonly IEmailSender _email;
        private readonly IIntegrationSettingsService _settings;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(UserManager<AppUser> users, IEmailSender email,
            IIntegrationSettingsService settings, ILogger<PasswordResetService> logger)
        {
            _users = users; _email = email; _settings = settings; _logger = logger;
        }

        public async Task RequestAsync(string email, CancellationToken ct = default)
        {
            email = (email ?? "").Trim();
            if (email.Length == 0) return;

            var user = await _users.FindByEmailAsync(email);
            if (user is null || string.IsNullOrWhiteSpace(user.Email) || user.StatusId != 1)
            {
                _logger.LogInformation("Password reset requested for an unknown or inactive email");
                return;
            }

            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var code = ToBase64Url(token);
            var baseUrl = ((await _settings.GetRawAsync("Auth.PasswordResetBaseUrl", ct)) ?? DefaultBaseUrl).Trim().TrimEnd('/');
            var link = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={code}";

            var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(user.DisplayName) ? "there" : user.DisplayName);
            var href = WebUtility.HtmlEncode(link);
            var body = $@"<div style=""font-family:Arial,sans-serif;font-size:15px;color:#222;line-height:1.5"">
<p>Hi {name},</p>
<p>We received a request to reset the password for your Axis account.</p>
<p><a href=""{href}"" style=""display:inline-block;background:#4f46e5;color:#fff;padding:12px 22px;border-radius:8px;text-decoration:none;font-weight:bold"">Reset password</a></p>
<p>Or open this link: <br><a href=""{href}"">{href}</a></p>
<p>The link works once and expires in 2 hours. If you didn't ask for this, ignore this email; your password stays the same.</p>
<p>— Axis</p></div>";

            var res = await _email.SendAsync(new[] { user.Email }, "Reset your Axis password", body, isHtml: true, ct: ct);
            if (!res.Sent)
                _logger.LogWarning("Password reset email for user {UserId} not sent: {Error}", user.Id, res.Error);
        }

        public async Task<BaseResponse> ResetAsync(string email, string token, string newPassword, CancellationToken ct = default)
        {
            const string badLink = "This reset link is invalid or has expired. Request a new one.";
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                return new(false, badLink);
            if (string.IsNullOrEmpty(newPassword))
                return new(false, "Enter a new password.");

            var user = await _users.FindByEmailAsync(email.Trim());
            if (user is null || user.StatusId != 1) return new(false, badLink);

            string decoded;
            try { decoded = FromBase64Url(token.Trim()); }
            catch (FormatException) { return new(false, badLink); }

            var result = await _users.ResetPasswordAsync(user, decoded, newPassword);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                    return new(false, badLink);
                return new(false, string.Join(" ", result.Errors.Select(e => e.Description)));
            }

            // A forgotten password often follows lockouts from failed tries.
            await _users.ResetAccessFailedCountAsync(user);
            await _users.SetLockoutEndDateAsync(user, null);
            _logger.LogInformation("Password reset completed for user {UserId}", user.Id);
            return new(true, null);
        }

        // Identity tokens contain '+', '/' and '='; base64url keeps the link intact.
        internal static string ToBase64Url(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        internal static string FromBase64Url(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
    }
}
