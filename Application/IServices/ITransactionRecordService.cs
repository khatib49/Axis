using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<TransactionDto?> GetAsync(int id, CancellationToken ct = default);
        Task<TransactionDto?> GetWithItemsAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<TransactionDto> CreateAsync(TransactionCreateDto dto, string createdBy, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, TransactionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<TransactionDto> CreateCoffeeShopOrder(List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct);

        Task<TransactionDto> CreateGameSession(int gameId, int gameSettingId, int hours, int statusId, string createdBy, int roomSetId, CancellationToken ct = default);

        Task<PaginatedResponse<ItemTransactionDto>> GetItemTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);

        Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);
    }
}
