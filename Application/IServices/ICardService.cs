using Application.DTOs;

namespace Application.IServices
{
    public interface ICardService
    {
        Task<CardDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<CardDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<CardDto> CreateAsync(CardCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, CardUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
