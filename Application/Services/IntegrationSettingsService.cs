using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    /// <summary>
    /// Key/value store for integration credentials. Secrets are masked
    /// in API responses — only the last 4 chars are exposed for visual
    /// confirmation. The raw value never crosses the controller boundary.
    /// </summary>
    public class IntegrationSettingsService : IIntegrationSettingsService
    {
        private readonly IBaseRepository<IntegrationSetting> _repo;
        private readonly IUnitOfWork _uow;
        private readonly IHttpClientFactory _httpFactory;

        public IntegrationSettingsService(
            IBaseRepository<IntegrationSetting> repo,
            IUnitOfWork uow,
            IHttpClientFactory httpFactory)
        {
            _repo = repo;
            _uow = uow;
            _httpFactory = httpFactory;
        }

        public async Task<IReadOnlyList<IntegrationSettingDto>> ListAsync(CancellationToken ct = default)
        {
            var rows = await _repo.Query()
                .OrderBy(x => x.Key)
                .ToListAsync(ct);

            return rows.Select(r =>
            {
                var hasValue = !string.IsNullOrEmpty(r.Value);
                var display = r.IsSecret
                    ? (hasValue ? Mask(r.Value!) : null)
                    : r.Value;

                return new IntegrationSettingDto(
                    r.Id, r.Key, display, r.IsSecret, hasValue,
                    r.Description, r.UpdatedBy, r.UpdatedOn);
            }).ToList();
        }

        public async Task<string?> GetRawAsync(string key, CancellationToken ct = default)
        {
            var row = await _repo.Query()
                .FirstOrDefaultAsync(x => x.Key == key, ct);
            return string.IsNullOrEmpty(row?.Value) ? null : row.Value;
        }

        public async Task UpsertAsync(string key, string? value, string? actor, CancellationToken ct = default)
        {
            // Find via Query (tracked) so EF can detect update.
            var row = await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(x => x.Key == key, ct);

            if (row == null)
            {
                row = new IntegrationSetting
                {
                    Key = key,
                    Value = string.IsNullOrEmpty(value) ? null : value,
                    IsSecret = LooksSecret(key),
                    UpdatedBy = actor,
                    UpdatedOn = DateTime.UtcNow
                };
                await _repo.AddAsync(row, ct);
            }
            else
            {
                // Empty string clears the value.
                row.Value = string.IsNullOrEmpty(value) ? null : value;
                row.UpdatedBy = actor;
                row.UpdatedOn = DateTime.UtcNow;
                _repo.Update(row);
            }

            await _uow.SaveChangesAsync(ct);
        }

        // ── Test endpoints ─────────────────────────────────────────────
        public async Task<IntegrationTestResultDto> TestAnthropicAsync(CancellationToken ct = default)
        {
            var key = await GetRawAsync("Anthropic.ApiKey", ct);
            if (string.IsNullOrWhiteSpace(key))
                return new IntegrationTestResultDto(false, "Anthropic.ApiKey is not set.");

            var model = await GetRawAsync("Anthropic.Model", ct) ?? "claude-sonnet-4-6";

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.Add("x-api-key", key);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model,
                max_tokens = 16,
                messages = new[] { new { role = "user", content = "ping" } }
            };

            try
            {
                var resp = await http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync(ct);
                    return new IntegrationTestResultDto(false, $"HTTP {(int)resp.StatusCode}: {Truncate(err, 300)}");
                }
                return new IntegrationTestResultDto(true, $"Anthropic OK (model: {model})");
            }
            catch (Exception ex)
            {
                return new IntegrationTestResultDto(false, ex.Message);
            }
        }

        public async Task<IntegrationTestResultDto> TestWhatsAppAsync(CancellationToken ct = default)
        {
            var phoneId = await GetRawAsync("WhatsApp.PhoneNumberId", ct);
            var token   = await GetRawAsync("WhatsApp.AccessToken", ct);

            if (string.IsNullOrWhiteSpace(phoneId) || string.IsNullOrWhiteSpace(token))
                return new IntegrationTestResultDto(false, "PhoneNumberId or AccessToken is missing.");

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                // GET /{phoneId} returns the phone number's metadata if creds work
                var resp = await http.GetAsync($"https://graph.facebook.com/v19.0/{phoneId}", ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    return new IntegrationTestResultDto(false, $"HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}");
                return new IntegrationTestResultDto(true, "WhatsApp OK");
            }
            catch (Exception ex)
            {
                return new IntegrationTestResultDto(false, ex.Message);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────
        private static string Mask(string raw)
        {
            if (raw.Length <= 4) return "••••";
            return "••••••••" + raw[^4..];
        }

        // Defensive: even if a row exists without IsSecret set, infer from name.
        private static bool LooksSecret(string key)
            => key.Contains("Key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Password", StringComparison.OrdinalIgnoreCase);

        private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
    }
}
