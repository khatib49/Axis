using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<List<GameHourlySalesDto>> GetGameHourlySalesAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);
        Task<List<ItemSalesReportDto>> GetItemSalesReportAsync(DateTime? from,DateTime? to, string? categoryIds, int top, CancellationToken ct = default);
        Task<int> GetOrdersCountAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);
        Task<PeriodTotalsDto> GetTotalsAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);
        Task<List<DailySalesDto>> GetDailySalesAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default);
        Task<RoomSetsAvailabilityDto?> GetRoomSetsAvailability(int roomId, int ongoingStatusId = 1, CancellationToken ct = default);
        Task<TransactionDto?> GetAsync(int id, CancellationToken ct = default);
        Task<TransactionDto?> GetWithItemsAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<TransactionDto> CreateAsync(TransactionCreateDto dto, string createdBy, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, TransactionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<TransactionDto>> CreateCoffeeShopOrder(int? userId, int discountId, List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct, string comment = "");
        Task<BaseResponse<TransactionDto>> CreateGameSession(int? userId, int gameId, int gameSettingId, int hours, int statusId, string createdBy, int roomSetId, int discountId,
            CancellationToken ct = default, int numberOfPersons = 1, bool isDayPass = false, string comment = "");
        Task<PaginatedResponse<ItemTransactionDto>> GetItemTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);

        Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);
        Task<BaseResponse<TransactionDto>> CloseGameSession(int invoiceId,string updatedBy,CancellationToken ct = default);
        Task<BaseResponse<List<TransactionDto>>> GetOpenBoardGameSessions(CancellationToken ct = default);
        Task<BaseResponse<List<TransactionDto>>> GetOpenPs5Sessions(CancellationToken ct = default);

    }
}
