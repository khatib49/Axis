using Application.DTOs;

namespace Application.IServices
{
    public interface IStatusService
    {
        Task<StatusDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<StatusDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<StatusDto> CreateAsync(StatusCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, StatusUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
