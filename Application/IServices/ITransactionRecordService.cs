using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<TransactionDto>> ListAsync(CancellationToken ct = default);
        Task<TransactionDto> CreateAsync(TransactionCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, TransactionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
