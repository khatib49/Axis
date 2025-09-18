using Application.DTOs;

namespace Application.IServices
{
    public interface ISettingsValueService
    {
        Task<SettingsValueDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<SettingsValueDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<SettingsValueDto> CreateAsync(SettingsValueCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, SettingsValueUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
