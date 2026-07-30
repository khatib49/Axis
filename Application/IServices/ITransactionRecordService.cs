using Application.DTOs;

namespace Application.IServices
{
    public interface ITransactionRecordService
    {
        Task<BaseResponse<bool>> RemoveItemFromOpenInvoiceAsync(int transactionId, int itemId, CancellationToken ct = default);
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

        /// <summary>
        /// Narrow endpoint used by the cashier UI to attach / detach a
        /// client on an open session card. Same net effect as UpdateAsync
        /// with only UserId set, but doesn't require the admin role that
        /// the full PUT endpoint has. userId = null clears the client,
        /// > 0 sets it, 0 clears it.
        /// </summary>
        Task<bool> AttachClientAsync(int transactionId, int? userId, CancellationToken ct = default);

        /// <summary>
        /// ADMIN-ONLY full replacement of a transaction's item lines,
        /// regardless of status (open OR closed) and type (game OR FNB).
        /// Diffs the incoming list against current TransactionItems and:
        ///   - new/increased lines → consumes Item.Quantity + ingredient stock
        ///   - removed/decreased lines → restores both
        ///   - TotalPrice adjusted by the net delta (discount-aware)
        /// Every change is written to the transaction audit log.
        /// </summary>
        Task<BaseResponse<TransactionDto>> ReplaceTransactionItemsAsync(
            int transactionId,
            IReadOnlyList<(int itemId, int quantity)> lines,
            string actor,
            CancellationToken ct = default);
        Task<BaseResponse<TransactionDto>> UpdateOpenInvoiceSet(int invoiceId, int? setId, string updatedBy, CancellationToken ct);
        Task<BaseResponse<TransactionDto>> CreateCoffeeShopOrder(int? userId, int discountId, List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct, string comment = "", bool isOpenInvoice = false, int? setId = null, int? channelId = null);
        Task<BaseResponse<TransactionDto>> CreateGameSession(int? userId, int gameId, int gameSettingId, int hours, int statusId, string createdBy, int roomSetId, int discountId,
            CancellationToken ct = default, int numberOfPersons = 1, bool isDayPass = false, string comment = "");
        Task<PaginatedResponse<ItemTransactionDto>> GetItemTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);

        Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default);
        Task<BaseResponse<TransactionDto>> CloseGameSession(int invoiceId,string updatedBy,CancellationToken ct = default);

        // Main-dashboard list: filter by created date + optional channel.
        // Returns a flat row shape suitable for both the table render and
        // the Excel export.
        Task<PaginatedResponse<DashboardTransactionRowDto>> GetDashboardTransactionsAsync(
            DashboardTransactionsFilterDto filter,
            CancellationToken ct = default);
        Task<BaseResponse<List<TransactionDto>>> GetOpenBoardGameSessions(CancellationToken ct = default);
        Task<BaseResponse<List<TransactionDto>>> GetOpenPs5Sessions(CancellationToken ct = default);
        Task<BaseResponse<List<TransactionDto>>> GetOpenFnbInvoices(CancellationToken ct = default);
        Task<BaseResponse<TransactionDto>> AddItemsToOpenInvoice( int invoiceId, List<OrderItemRequest> itemsRequest, string updatedBy,CancellationToken ct); 
        Task<BaseResponse<TransactionDto>> CloseOpenInvoice( int invoiceId, string updatedBy, CancellationToken ct);

    }
}
