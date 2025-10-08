using Application.DTOs;

namespace Application.IServices
{
    public interface ISettingService
    {
        Task<SettingDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<SettingDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<SettingDto> CreateAsync(SettingCreateDto dto, string? CreatedBy, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, SettingUpdateDto dto, string? ModifiedBy, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
