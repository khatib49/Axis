using Application.DTOs;

namespace Application.IServices
{
    public interface IPassTypeService
    {
        Task<PassTypeDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<PassTypeDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<PassTypeDto> CreateAsync(PassTypeCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, PassTypeUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
