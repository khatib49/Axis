using Application.DTOs;

namespace Application.IServices
{
    public interface ICardService
    {
        Task<CardDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<CardDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<CardDto> CreateAsync(CardCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, CardUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
