using Application.DTOs;

namespace Application.IServices
{
    public interface IPassTypeService
    {
        Task<PassTypeDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<PassTypeDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<PassTypeDto> CreateAsync(PassTypeCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, PassTypeUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
