using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<TransactionDto> CreateGameSession(int gameId, int gameSettingId, int hours, int statusid, string createdBy, CancellationToken ct = default);
        Task<TransactionDto?> GetAsync(int id, CancellationToken ct = default);
        Task<TransactionDto?> GetWithItemsAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<TransactionDto> CreateAsync(TransactionCreateDto dto, string createdBy, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, TransactionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<TransactionDto> CreateCoffeeShopOrder(List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct);
    }
}
