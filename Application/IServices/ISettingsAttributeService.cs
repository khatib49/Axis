using Application.DTOs;

namespace Application.IServices
{
    public interface ISettingsAttributeService
    {
        Task<SettingsAttributeDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<SettingsAttributeDto>> ListAsync(CancellationToken ct = default);
        Task<SettingsAttributeDto> CreateAsync(SettingsAttributeCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, SettingsAttributeUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
