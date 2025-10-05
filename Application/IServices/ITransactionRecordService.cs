using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<TransactionDto> CreateGameSession(Guid gameId, Guid gameSettingId, int hours, Guid statusid, string createdBy, CancellationToken ct = default);
        Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<TransactionDto?> GetWithItemsAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<TransactionDto> CreateAsync(TransactionCreateDto dto, string createdBy, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, TransactionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
        Task<TransactionDto> CreateCoffeeShopOrder(List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct);
    }
}
