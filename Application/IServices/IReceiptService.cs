using Application.DTOs;

namespace Application.IServices
{
    public interface IReceiptService
    {
        Task<ReceiptDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<ReceiptDto>> ListAsync(CancellationToken ct = default);
        Task<ReceiptDto> CreateAsync(ReceiptCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, ReceiptUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
