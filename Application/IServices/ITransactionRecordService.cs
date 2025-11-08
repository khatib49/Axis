using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<PeriodTotalsDto> GetTotalsAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);
        Task<List<DailySalesDto>> GetDailySalesAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);
        Task<RoomSetsAvailabilityDto?> GetRoomSetsAvailability( int roomId, int ongoingStatusId = 1, CancellationToken ct = default);
        Task<TransactionDto?> GetAsync(int id, CancellationToken ct = default);
        Task<TransactionDto?> GetWithItemsAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<TransactionDto> CreateAsync(TransactionCreateDto dto, string createdBy, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, TransactionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<TransactionDto>> CreateCoffeeShopOrder(List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct);

        Task<BaseResponse<TransactionDto>> CreateGameSession(int gameId, int gameSettingId, int hours, int statusId, string createdBy, int roomSetId, CancellationToken ct = default);

        Task<PaginatedResponse<ItemTransactionDto>> GetItemTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);

        Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);
    }
}
