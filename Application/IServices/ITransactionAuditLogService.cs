using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionAuditLogService
    {
        Task<List<TransactionAuditLogDto>> GetByTransaction(int transactionId, CancellationToken ct = default);
        Task<PaginatedResponse<TransactionAuditLogDto>> ListAll(int page, int pageSize, CancellationToken ct = default);
    }
}
