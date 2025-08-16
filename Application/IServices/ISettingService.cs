using Application.DTOs;

namespace Application.IServices
{
    public interface ISettingService
    {
        Task<SettingDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<SettingDto>> ListAsync(CancellationToken ct = default);
        Task<SettingDto> CreateAsync(SettingCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, SettingUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
