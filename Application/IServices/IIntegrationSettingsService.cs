using Application.DTOs;

namespace Application.IServices
{
    public interface IIntegrationSettingsService
    {
        // List all settings — secret values are masked.
        Task<IReadOnlyList<IntegrationSettingDto>> ListAsync(CancellationToken ct = default);

        // Get a single setting's raw value (used by internal services that
        // actually need the secret — controllers/UI never use this).
        Task<string?> GetRawAsync(string key, CancellationToken ct = default);

        Task UpsertAsync(string key, string? value, string? actor, CancellationToken ct = default);

        // Try calling Anthropic with the stored key
        Task<IntegrationTestResultDto> TestAnthropicAsync(CancellationToken ct = default);

        // Try a no-op WhatsApp call (account info) with the stored creds
        Task<IntegrationTestResultDto> TestWhatsAppAsync(CancellationToken ct = default);
    }
}
