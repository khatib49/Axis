using Application.DTOs;

namespace Application.IServices
{
    public interface IReceiptService
    {
        Task<ReceiptDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<ReceiptDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<ReceiptDto> CreateAsync(ReceiptCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, ReceiptUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
