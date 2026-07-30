using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Application.Middleware;
using Application.Services.SignalR;
using Domain.Entities;
using Domain.Identity;
using Hangfire;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Application.Services
{
    public partial class TransactionRecordService : ITransactionRecordService
    {
        private readonly IBaseRepository<TransactionRecord> _repo;
        private readonly IBaseRepository<Setting> _repoSetting;
        private readonly IBaseRepository<Room> _repoRoom;
        private readonly IBaseRepository<Game> _repoGame;
        private readonly IBaseRepository<Item> _repoItem;
        private readonly IBaseRepository<Status> _repoStatus;
        private readonly IBaseRepository<TransactionItem> _repoTrxItem;
        private readonly IBaseRepository<Set> _repoSet;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<TransactionRecordService> _logger;
        private readonly IBaseRepository<Discount> _repoDiscount;
        private readonly IJournalService _journalService;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILoyaltyService _loyaltyService;
        private readonly IBaseRepository<KitchenBarOrder> _repoKitchenBar;
        private readonly IHubContext<KitchenBarHub> _hubContext;
        private readonly IBaseRepository<TransactionAuditLog> _repoAuditLog;
        private readonly IPrintDispatchService _printDispatch;
        // Permanent admin-action log — no FK to TransactionRecord, so entries
        // survive a transaction delete (TransactionAuditLog cascades and dies
        // along with its parent, which is no good for "who deleted this tx").
        private readonly IBaseRepository<AdminAuditLog> _repoAdminAuditLog;
        // Used to identify recipe-driven items so the Item.Quantity out-of-
        // stock check can be bypassed — for recipe items the real stock is
        // on Ingredients, not on Item.Quantity.
        private readonly IBaseRepository<RecipeLine> _repoRecipeLine;
        private readonly IStockService _stockService;
        public TransactionRecordService(IBaseRepository<TransactionRecord> repo, IBaseRepository<Setting> repoSetting,
            IBaseRepository<Room> repoRoom, IBaseRepository<Game> repoGame, IBaseRepository<Item> repoItem,
            IBaseRepository<TransactionItem> repoTrxItem, IBaseRepository<Status> repoStatus, UserManager<AppUser> userManager,
            IBaseRepository<Discount> repoDiscount, IBaseRepository<Set> repoSet, ILoyaltyService loyaltyService,
        IUnitOfWork uow, DomainMapper mapper, ILogger<TransactionRecordService> logger, IHttpContextAccessor httpContextAccessor,
        IJournalService journalService, IBaseRepository<KitchenBarOrder> repoKitchenBar, IHubContext<KitchenBarHub> hubContext,
        IBaseRepository<TransactionAuditLog> repoAuditLog,
        IBaseRepository<AdminAuditLog> repoAdminAuditLog,
        IBaseRepository<RecipeLine> repoRecipeLine,
        IStockService stockService, IPrintDispatchService printDispatch)
        {
            _repoAuditLog = repoAuditLog;
            _printDispatch = printDispatch;
            _repoAdminAuditLog = repoAdminAuditLog;
            _repoRecipeLine = repoRecipeLine;
            _hubContext = hubContext;
            _loyaltyService = loyaltyService;
            _repo = repo; _uow = uow; _mapper = mapper;
            _userManager = userManager;
            _repoSetting = repoSetting;
            _repoKitchenBar = repoKitchenBar;
            _repoRoom = repoRoom;
            _repoGame = repoGame;
            _repoItem = repoItem;
            _repoTrxItem = repoTrxItem;
            _repoStatus = repoStatus;
            _repoDiscount = repoDiscount;
            _repoSet = repoSet;
            _logger = logger;
            _journalService = journalService;
            _http = httpContextAccessor;
            _stockService = stockService;
        }

        public async Task<BaseResponse<bool>> RemoveItemFromOpenInvoiceAsync(int transactionId, int itemId, CancellationToken ct = default)
        {
            var tx = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

            if (tx is null)
                return new BaseResponse<bool>(false, "Not found", "Transaction not found.", false);

            //if (tx.StatusId != 3) // must be open invoice
            //    return new BaseResponse<bool>(false, "Invalid status", "Only open invoices can be modified.", false);

            var itemToRemove = tx.TransactionItems.FirstOrDefault(ti => ti.ItemId == itemId);
            if (itemToRemove is null)
                return new BaseResponse<bool>(false, "Not found", "Item not found in this transaction.", false);

            var actor = _http?.HttpContext?.User?.Identity?.Name ?? "system";
            var removedQty = itemToRemove.Quantity;

            // 1) Item.Quantity counter (legacy per-item stock)
            var dbItem = await _repoItem.GetByIdAsync(itemId, asNoTracking: false, ct);
            if (dbItem is not null)
                dbItem.Quantity += removedQty;

            // 2) Ingredient.QuantityOnHand via recipe — restore exactly
            //    what this line consumed. Skips silently for non-recipe
            //    items, same as ConsumeForOrderAsync. If this throws, log
            //    and continue rather than block the line removal from
            //    persisting (otherwise the UI is stuck in a half-state).
            try
            {
                await _stockService.RestoreForLinesAsync(
                    transactionId,
                    new[] { (itemId, (decimal)removedQty) },
                    reason: $"Item removed from invoice #{transactionId}",
                    actor: actor,
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Stock restore failed when removing item {ItemId} from invoice {InvoiceId}. Item line will still be removed; manual ingredient adjustment may be needed.",
                    itemId, transactionId);
            }

            // Recalculate total
            tx.TotalPrice -= removedQty * (dbItem?.Price ?? 0);
            if (tx.TotalPrice < 0) tx.TotalPrice = 0;

            _repoTrxItem.Remove(itemToRemove);
            tx.ModifiedOn = DateTime.UtcNow;

            // Audit — TransactionAuditLog survives because the parent isn't
            // deleted here; only the line is removed.
            await LogAuditAsync(
                transactionId: transactionId,
                changedBy: actor,
                action: "LineRemoved",
                fieldChanged: "TransactionItems",
                oldValue: $"{dbItem?.Name ?? "Item#" + itemId} x{removedQty}",
                newValue: "(removed)",
                notes: $"NewTotal={tx.TotalPrice:F2}",
                ct: ct);

            await _uow.SaveChangesAsync(ct);
            return new BaseResponse<bool>(true, null, "Item removed successfully.", true);
        }

        public async Task<RoomSetsAvailabilityDto?> GetRoomSetsAvailability(int roomId, int ongoingStatusId = 1, CancellationToken ct = default)
        {
            // Load room with its sets
            var room = await _repoRoom.Query()
                .Include(r => r.Sets)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roomId, ct);

            if (room is null) return null; // caller returns 404

            // Which sets are currently "busy" (there exists an ongoing transaction using them)
            var busySetIds = await _repo.Query()
                .AsNoTracking()
                .Where(t => t.RoomId == roomId
                         && t.Set != null
                         && t.StatusId == ongoingStatusId)
                .Select(t => t.SetId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var available = new List<SetDto>();
            var unavailable = new List<SetDto>();

            foreach (var rs in room.Sets)
            {
                var dto = new SetDto { Id = rs.Id, Name = rs.Name };
                if (busySetIds.Contains(rs.Id))
                    unavailable.Add(dto);
                else
                    available.Add(dto);
            }

            return new RoomSetsAvailabilityDto
            {
                RoomId = roomId,
                Available = available.OrderBy(x => x.Name).ToList(),
                Unavailable = unavailable.OrderBy(x => x.Name).ToList()
            };
        }
        public async Task<TransactionDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query()
                    .Include(s => s.Game)
                    .Include(s => s.GameType)
                    .Include(s => s.GameSetting)
                    .Include(s => s.Room)
                    .Include(s => s.Status)
                    .Include(s => s.Set) // Include Set
                    .AsSplitQuery()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == id, ct);
            return e is null ? null : _mapper.ToDto(e);
        }
        public async Task<TransactionDto?> GetWithItemsAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query()
                .Include(s => s.Game)
                .Include(s => s.GameType)
                .Include(s => s.GameSetting)
                .Include(s => s.Room)
                .Include(s => s.Status)
                .Include(s => s.Set) // Include Set
                .Include(s => s.Discount)
                .Include(s => s.Channel) // sales channel (e.g. Toters)
                .Include(s => s.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                        .ThenInclude(i => i.CoffeeShopOrders)
                            .ThenInclude(co => co.User) // if you want user name
                            .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (e is null) return null;

            return new TransactionDto(
                e.Id,
                e.RoomId,
                e.Room?.Name ?? string.Empty,
                e.GameTypeId,
                e.GameType?.Name ?? string.Empty,
                e.GameId,
                e.Game?.Name ?? string.Empty,
                e.GameSettingId,
                e.GameSetting?.Name ?? string.Empty,
                e.Hours,
                e.TotalPrice,
                e.StatusId,
                e.CreatedOn,
                e.ModifiedOn,
                e.CreatedBy,
                e.TransactionItems.Select(ti => new TransactionItemDto(
                    ti.ItemId,
                    ti.Item.Name,
                    ti.Quantity,
                    ti.Item.Price,
                    ti.Item.Type,
                    ti.Item.CoffeeShopOrders.Select(co => new CoffeeShopOrderDto(
                        co.Id,
                        co.UserId,
                        co.CardId,
                        co.ItemId,
                        co.Quantity,
                        co.Price,
                        co.Timestamp
                    )).ToList()
                )).ToList(),
                e.SetId,
                e.Set?.Name ?? string.Empty,
                e.DiscountId,
                e.Discount?.Percentage,
                e.Discount?.Name ?? string.Empty,
                e.numberOfPersons,
                e.GameSetting?.IsDayPass ?? false,
                e.Comment,
                null,
                null,
                e.ChannelId,
                e.Channel != null ? e.Channel.Name : null
            );
        }
        public async Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            // Start with base query
            var query = _repo.QueryableAsync()
                        .Include(s => s.Game)
                        .Include(s => s.GameType)
                        .Include(s => s.GameSetting)
                        .Include(s => s.Room)
                        .Include(s => s.Status)
                        .Include(s => s.Set)
                        .AsSplitQuery()// Include Set
                        .AsNoTracking();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(pagination.createdBy))
            {
                query = query.Where(x => x.CreatedBy == pagination.createdBy);
            }


            // Count at database level (before pagination)
            var totalCount = await query.CountAsync(ct);

            // Paginate at database level
            var pagedList = await query
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToListAsync(ct);

            // Map to DTOs
            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<TransactionDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }

        public async Task<TransactionDto> CreateAsync(TransactionCreateDto dto, string createdBy, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            e.CreatedBy = createdBy ?? "";
            e.CreatedOn = DateTime.UtcNow;
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<BaseResponse<TransactionDto>> CreateGameSession(int? userId, int gameId, int gameSettingId, int hours, int statusId,
                string createdBy, int roomSetId, int discountId, CancellationToken ct = default, int numberOfPersons = 1, bool isDayPass = false, string comment = "")
        {
            var reqId = GetReqId();
            var sig = HashObject(new { gameId, gameSettingId, hours, statusId, createdBy, roomSetId });

            // 1) Validate game
            var game = await _repoGame.Query().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == gameId, ct);
            if (game is null)
                return new BaseResponse<TransactionDto>(false, "Invalid game ID", "The specified game does not exist.");

            // 2) Find any room by game category (your rule)
            var room = await _repoRoom.Query()
                    .Include(r => r.Sets)  // need sets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.CategoryId == game.CategoryId, ct);


            if (room is null)
                return new BaseResponse<TransactionDto>(false, "Invalid room ID", "No available room for the selected game type.");

            if (room.IsOpenSet && roomSetId > 0)
                return new BaseResponse<TransactionDto>(false, "Invalid set selection", "This game requires open set.");

            if (!room.IsOpenSet)
            {
                // 3) Validate the chosen RoomSet belongs to this room
                var set = room.Sets.FirstOrDefault(s => s.Id == roomSetId);
                if (set is null)
                    return new BaseResponse<TransactionDto>(false, "invalid set id", "Invalid set ID for the selected room.");

                // 4) Ensure this set is not already in use for an ongoing transaction
                var isSetInUse = await _repo.Query().AsNoTracking()
                    .AnyAsync(s => s.RoomId == room.Id && s.SetId == roomSetId && s.StatusId == statusId, ct);
                if (isSetInUse)
                    return new BaseResponse<TransactionDto>(false, "set in use", "The selected set is currently in use. Please choose a different set.");


                Set setToUpdate = new Set { Id = set.Id };

                _repoSet.Attach(setToUpdate);
                setToUpdate.StatusId = 10;
                await _uow.SaveChangesAsync(ct);

            }

            // 5) Price calc from Setting
            var setting = await _repoSetting.Query().AsNoTracking()
                .Where(s => s.Id == gameSettingId)
                .Select(s => new { s.Hours, s.Price, s.IsOpenHour, s.IsDayPass })
                .FirstOrDefaultAsync(ct);

            if (setting is null)
            {
                return new BaseResponse<TransactionDto>(false, "Invalid game setting", "The specified game setting does not exist.");
            }

            DateTime? expectedEndOn = null;

            decimal totalPrice = 0.0M;

            if (setting.IsOpenHour || setting.IsDayPass)
            {
                totalPrice = setting.Price;
            }

            if (numberOfPersons > 0)
            {
                totalPrice = totalPrice * numberOfPersons;
            }

            Discount? discount = null;
            if (discountId != 0)
            {
                // Apply Discount
                discount = await _repoDiscount.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == discountId, ct);

                if (discount is null)
                    return new BaseResponse<TransactionDto>(false, "Invalid discount", "The selected discount does not exist.");

                if (discount.IsActive)
                {
                    totalPrice -= (totalPrice * discount.Percentage / 100);

                    if (totalPrice < 0)
                        totalPrice = 0;
                }
            }

            #region to Check if it is for ps5 or board games to let the status be processed and unpaid
            int statusToUse = (game.CategoryId == 2 || game.CategoryId == 6) || isDayPass ? 7 : 6; // 5: processed and unpaid, 6: processed and paid
            if (setting.IsDayPass == true)
            {
                statusToUse = 6;
            }
            #endregion


            // 6) Create DTO -> Entity
            var createDto = new TransactionCreateDto(
                    RoomId: room.Id,
                    SetId: roomSetId,              // NEW
                    GameTypeId: game.CategoryId,
                    GameId: gameId,
                    GameSettingId: gameSettingId,
                    Hours: hours,
                    TotalPrice: totalPrice,
                    StatusId: statusToUse, //processed and paid
                    UserId: userId,
                    CreatedOn: DateTime.UtcNow,
                    CreatedBy: createdBy ?? string.Empty,
                    DiscountId: discount?.Id,
                    numberOfPersons: numberOfPersons,
                    Comment: comment
                );

            var e = _mapper.ToEntity(createDto);
            if (e.SetId == 0)
                e.SetId = null;

            //e.ExpectedEndOn = expectedEndOn;

            try
            {
                await _repo.AddAsync(e, ct);
                await _uow.SaveChangesAsync(ct);
                await LogAuditAsync(
                    transactionId: e.Id,  // Id is set after AddAsync
                    changedBy: createdBy,
                    action: "Created",
                    notes: $"GameId={gameId}, SettingId={gameSettingId}, Persons={numberOfPersons}, Status={statusToUse}, Total={totalPrice:F2}",
                    ct: ct
                );
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var (prov, code) = ExtractDbCode(ex);

                _logger.LogError(ex,
                    "GS/Session ERROR ReqId={ReqId} DB={Prov}:{Code} Game={Game} Setting={Setting} Set={Set} Sig={Sig} ",
                    reqId, prov, code, gameId, gameSettingId, roomSetId, sig);

                return new BaseResponse<TransactionDto>(false, "set in use", "The selected set just became in use. Please choose a different set.");
            }
            // ========================================
            // ✅ CALCULATE LOYALTY TICKETS
            // ========================================
            if (statusToUse == 6 && userId.HasValue)
            {
                try
                {
                    var userPhone = await GetUserPhoneNumberAsync(userId.Value, ct);
                    var userName = await GetUserFullNameAsync(userId.Value, ct);

                    if (!string.IsNullOrWhiteSpace(userPhone) && await IsClientUserAsync(userId.Value, ct))
                    {
                        var loyaltyRequest = new CalculateTicketsRequest
                        {
                            TransactionId = e.Id,
                            TotalAmount = totalPrice,
                            CustomerPhone = userPhone,
                            CustomerName = userName ?? createdBy // Use actual name or fallback to createdBy
                        };

                        var loyaltyResponse = await _loyaltyService.CalculateAndAssignTicketsAsync(loyaltyRequest);

                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "❌ Error calculating loyalty tickets: TxId={TxId}, User={UserId}",
                        e.Id, userId.Value);
                }
            }
            // ========================================


            e = await _repo.Query()
                .Include(x => x.Room)
                .Include(x => x.Game)
                .Include(x => x.GameType)
                .Include(x => x.GameSetting)
                .Include(x => x.Discount)
                .Include(x => x.Set)
                .Include(x => x.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                .Include(x => x.TransactionItems)
                    .ThenInclude(ti => ti.Item.CoffeeShopOrders)
                    .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == e.Id, ct);

            TransactionDto transactionDto = _mapper.ToDto(e);
            if (transactionDto.StatusId == 6) // Paid
            {
                try
                {
                    var journalResult = await _journalService.CreateJournalEntryFromTransactionAsync(
                        transactionDto.Id,
                        ct);

                    if (journalResult.Success)
                    {
                        _logger.LogInformation(
                            "Journal entry {EntryNumber} created for transaction {TxId}",
                            journalResult.Data?.EntryNumber,
                            transactionDto.Id);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to create journal entry for transaction {TxId}: {Error}",
                            transactionDto.Id,
                            journalResult.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Exception creating journal entry for transaction {TxId}",
                        transactionDto.Id);
                    // Don't fail the transaction, just log the error
                }
            }


            return new BaseResponse<TransactionDto>(true, null, "Game session created successfully.", transactionDto);


        }

        public async Task<BaseResponse<TransactionDto>> CreateCoffeeShopOrder(int? userId, int discountId, List<OrderItemRequest> itemsRequest,
            string createdBy, CancellationToken ct, string comment = "", bool isOpenInvoice = false, int? setId = null, int? channelId = null)
        {

            var reqId = GetReqId();
            var sig = itemsRequest is null ? "-" : ItemsSignature(itemsRequest);

            if (itemsRequest is null || itemsRequest.Count == 0)
                return new BaseResponse<TransactionDto>(false, "No items", "No items provided.");


            var requested = itemsRequest
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));


            var invalidQty = requested.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList();
            if (invalidQty.Any())
                return new BaseResponse<TransactionDto>(false, "Invalid quantity",
                    $"Invalid quantity (<=0) for items: {string.Join(", ", invalidQty)}");

            var ids = requested.Keys.ToList();


            var dbItems = await _repoItem.Query(false).Include(i => i.Category)
                .Where(i => ids.Contains(i.Id))
                .ToListAsync(ct);


            if (dbItems.Count != ids.Count)
            {
                var missing = ids.Except(dbItems.Select(i => i.Id)).ToList();
                return new BaseResponse<TransactionDto>(false, "Invalid items",
                    $"The following item IDs do not exist: {string.Join(", ", missing)}");
            }


            // Only enforce the Item.Quantity counter on items WITHOUT a
            // recipe. Recipe-driven items report their real stock via
            // Ingredient.QuantityOnHand — the consume-on-sale path will
            // decrement ingredients and produce warnings if any go
            // negative, but the sale itself is allowed even when the
            // (unused) Item.Quantity counter is 0.
            var recipeItemIds = new HashSet<int>(
                await _repoRecipeLine.Query()
                    .Where(r => ids.Contains(r.ItemId))
                    .Select(r => r.ItemId)
                    .Distinct()
                    .ToListAsync(ct));

            var outOfStock = new List<string>();
            foreach (var it in dbItems)
            {
                if (recipeItemIds.Contains(it.Id)) continue; // recipe covers it
                var need = requested[it.Id];
                if (it.Quantity < need)
                    outOfStock.Add($"{it.Name} (needs {need}, has {it.Quantity})");
            }
            if (outOfStock.Any())
                return new BaseResponse<TransactionDto>(false, "Out of stock",
                    $"The following items are out of stock or insufficient: {string.Join("; ", outOfStock)}");

            // Compute total
            decimal totalPrice = 0m;
            foreach (var it in dbItems)
            {
                var qty = requested[it.Id];
                totalPrice += (it.Price * qty);
            }

            Discount? discount = null;
            if (discountId != 0)
            {
                // Apply Discount
                discount = await _repoDiscount.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == discountId, ct);

                if (discount is null)
                    return new BaseResponse<TransactionDto>(false, "Invalid discount", "The selected discount does not exist.");

                if (discount.IsActive)
                {
                    totalPrice -= (totalPrice * discount.Percentage / 100);

                    if (totalPrice < 0)
                        totalPrice = 0;
                }
            }

            // In CreateCoffeeShopOrder
            //bool containsTcg = (await _repoItem.ListAsync()).Any(i =>
            //    i.Category.Name.ToLower().Contains("tcg")); // check if the item category contains "tcg"

            int statusId = isOpenInvoice ? 7 : 6;
            // Create transaction
            var trx = new TransactionRecord
            {

                RoomId = null,
                SetId = setId,
                GameTypeId = null,
                GameId = null,
                GameSettingId = null,
                Hours = 0,
                TotalPrice = totalPrice,
                StatusId = statusId,
                UserId = userId,
                CreatedBy = createdBy ?? "",
                CreatedOn = DateTime.UtcNow,
                DiscountId = discount?.Id,
                Comment = comment,
                FK_FoodStatusId = 11,
                // Optional sales channel (e.g. Toters). The cashier picks this
                // on the F&B order form when the order came in via an external
                // app; null means a normal walk-in / direct order.
                ChannelId = channelId,
            };


            var trxItems = new List<TransactionItem>();
            foreach (var it in dbItems)
            {
                var qty = requested[it.Id];
                trxItems.Add(new TransactionItem
                {
                    TransactionRecord = trx,

                    ItemId = it.Id,
                    Quantity = qty,
                });


                it.Quantity -= qty;
            }



            // Collected stock warnings (ingredients that went negative) so we
            // can surface them on the response to the cashier UI as a yellow
            // toast. Lives outside the try so it's available after the block.
            IReadOnlyList<StockConsumptionWarningDto> stockWarnings = Array.Empty<StockConsumptionWarningDto>();

            try
            {
                _logger.LogInformation("CS/Order BEFORE_SAVE ReqId={ReqId} Total={Total} Items={Count}", reqId, totalPrice, trxItems.Count);


                await _repo.AddAsync(trx, ct);
                await _repoTrxItem.AddRangeAsync(trxItems, ct);

                await _uow.SaveChangesAsync(ct);

                // ─── Stock consumption ──────────────────────────────────
                // After we have trx.Id, deduct ingredient stock based on the
                // recipes of each item sold. Items without a recipe are
                // silently skipped (per the rollout decision). Warnings list
                // any ingredient whose post-balance went negative; we surface
                // them to the cashier UI but the sale still goes through.
                // SaveChanges below batches the stock updates with the rest
                // of the order so it's all atomic.
                try
                {
                    stockWarnings = await _stockService.ConsumeForOrderAsync(
                        trx.Id,
                        trxItems.Select(ti => (ti.ItemId, (decimal)ti.Quantity)).ToList(),
                        createdBy,
                        ct);
                }
                catch (Exception stockEx)
                {
                    // Never block a sale because stock tracking failed. Log
                    // loudly so the chef can investigate.
                    _logger.LogError(stockEx,
                        "CS/Order STOCK_FAILED ReqId={ReqId} TxId={TxId}", reqId, trx.Id);
                }

                // Resolve the attached client's display name so the kitchen
                // and bar tickets show WHO the order is for ("Guest: Anthony
                // Khoury"). Null when no client is attached — the ticket
                // simply omits the Guest line in that case.
                string? guestName = null;
                if (trx.UserId.HasValue)
                {
                    var client = await _userManager.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == trx.UserId.Value, ct);
                    if (client != null)
                    {
                        guestName = !string.IsNullOrWhiteSpace(client.DisplayName) ? client.DisplayName
                            : !string.IsNullOrWhiteSpace($"{client.FirstName} {client.LastName}".Trim()) ? $"{client.FirstName} {client.LastName}".Trim()
                            : client.UserName;
                    }
                }

                await CreateKitchenBarOrdersAsync(trx, trxItems, createdBy,
                    tableNumber: null, guestName: guestName, ct);

                await _uow.SaveChangesAsync(ct);

                // Push ESC/POS tickets to every configured kitchen/bar printer via the
                // on-site print agent. Fire-and-forget-safe: never throws, so a downed
                // printer or offline agent can't fail the sale.
                await _printDispatch.DispatchOrderTicketsAsync(trx.Id, createdBy,
                    tableNumber: null, guestName: guestName, ct);

                await LogAuditAsync(
                        transactionId: trx.Id,
                        changedBy: createdBy,
                        action: "Created",
                        notes: $"FNB order. Items={trxItems.Count}, Total={totalPrice:F2}, IsOpenInvoice={isOpenInvoice}",
                        ct: ct
                    );

                await _uow.SaveChangesAsync(ct);

            }
            catch (Exception ex)
            {
                var (prov, code) = ExtractDbCode(ex);
                _logger.LogError(ex,
                    "CS/Order ERROR ReqId={ReqId} DB={Prov}:{Code} Total={Total} Items={Count} Sig={Sig}",
                    reqId, prov, code, totalPrice, trxItems.Count, sig);

                return new BaseResponse<TransactionDto>(false, "Error happened", "Error happened");
            }
            // ========================================
            // ✅ CALCULATE LOYALTY TICKETS
            // ========================================
            string userName = "";
            if (userId.HasValue && !isOpenInvoice)
            {
                try
                {
                    var userPhone = await GetUserPhoneNumberAsync(userId.Value, ct);
                    userName = await GetUserFullNameAsync(userId.Value, ct);

                    if (!string.IsNullOrWhiteSpace(userPhone) && await IsClientUserAsync(userId.Value, ct))
                    {
                        var loyaltyRequest = new CalculateTicketsRequest
                        {
                            TransactionId = trx.Id,
                            TotalAmount = totalPrice,
                            CustomerPhone = userPhone,
                            CustomerName = userName ?? createdBy
                        };

                        var loyaltyResponse = await _loyaltyService.CalculateAndAssignTicketsAsync(loyaltyRequest);

                        if (loyaltyResponse.Success)
                        {
                            _logger.LogInformation(
                                "✅ Loyalty tickets assigned: TxId={TxId}, User={UserId}, Phone={Phone}, Tickets={Tickets}, Balance=${Balance:F2}",
                                trx.Id, userId.Value, userPhone, loyaltyResponse.TicketsEarned, loyaltyResponse.PendingBalance);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "⚠️ Loyalty calculation failed: TxId={TxId}, User={UserId}, Reason={Message}",
                                trx.Id, userId.Value, loyaltyResponse.Message);
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "ℹ️ No phone number for loyalty: TxId={TxId}, User={UserId}",
                            trx.Id, userId.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "❌ Error calculating loyalty tickets: TxId={TxId}, User={UserId}",
                        trx.Id, userId.Value);
                }
            }
            // ========================================


            var reloaded = await _repo.Query()
      .AsNoTracking()
      .Include(t => t.Room)
      .Include(t => t.Game)
      .Include(t => t.GameType)
      .Include(t => t.GameSetting)
      .Include(t => t.Discount)
      .Include(t => t.Set)
      .Include(t => t.User)  // IMPORTANT: Include user details
      .Include(t => t.TransactionItems)
          .ThenInclude(ti => ti.Item)
      .FirstOrDefaultAsync(t => t.Id == trx.Id, ct);

            if (reloaded == null)
                return new BaseResponse<TransactionDto>(false, "error",
                    "Transaction saved but could not reload.");

            TransactionDto transactionDto = _mapper.ToDto(reloaded);
            // Attach any stock warnings produced during consumption so the
            // cashier UI can show a yellow toast naming what went negative.
            if (stockWarnings != null && stockWarnings.Count > 0)
            {
                transactionDto = transactionDto with { StockWarnings = stockWarnings.ToList() };
            }
            try
            {
                var journalResult = await _journalService.CreateJournalEntryFromTransactionAsync(
                    transactionDto.Id,
                    ct);

                if (journalResult.Success)
                {
                    _logger.LogInformation(
                        "Journal entry {EntryNumber} created for FNB transaction {TxId}",
                        journalResult.Data?.EntryNumber,
                        transactionDto.Id);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to create journal entry for FNB transaction {TxId}: {Error}",
                        transactionDto.Id,
                        journalResult.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Exception creating journal entry for FNB transaction {TxId}",
                    transactionDto.Id);
                // Don't fail the transaction, just log the error
            }


            return new BaseResponse<TransactionDto>(true, null, "Item Order created successfully.", transactionDto);
        }

        public async Task<bool> UpdateAsync(int id, TransactionUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems) // for future-proofing; no stock ops here
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (e is null) return false;

            var roomChanged = dto.RoomId.HasValue && dto.RoomId.Value != e.RoomId;
            var setChanged = dto.SetId.HasValue && dto.SetId.Value != e.SetId;

            // Apply only provided fields
            var changedFields = new List<string>();
            if (dto.RoomId.HasValue) changedFields.Add($"RoomId={dto.RoomId}");
            if (dto.SetId.HasValue) changedFields.Add($"SetId={dto.SetId}");
            if (dto.GameTypeId.HasValue) changedFields.Add($"GameTypeId={dto.GameTypeId}");
            if (dto.GameId.HasValue) changedFields.Add($"GameId={dto.GameId}");
            if (dto.GameSettingId.HasValue) changedFields.Add($"GameSettingId={dto.GameSettingId}");
            if (dto.Hours.HasValue) changedFields.Add($"Hours={dto.Hours}");
            if (dto.TotalPrice.HasValue) changedFields.Add($"TotalPrice={dto.TotalPrice}");
            if (dto.StatusId.HasValue) changedFields.Add($"StatusId={dto.StatusId}");
            if (dto.DiscountId.HasValue) changedFields.Add($"DiscountId={dto.DiscountId}");
            if (dto.UserId.HasValue) changedFields.Add($"UserId={dto.UserId}");
            Discount? discount = null;
            var updatedBy = _http.HttpContext?.User?.Identity?.Name ?? "admin";
            if (dto.DiscountId.HasValue)
            {
                if (dto.DiscountId.Value > 0)
                {
                    discount = await _repoDiscount.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == dto.DiscountId.Value, ct)
                        ?? throw new ArgumentException("Invalid DiscountId.");

                    e.DiscountId = dto.DiscountId.Value;
                }
                else
                {
                    // remove discount
                    e.DiscountId = null;
                }
            }

            // Client attach/detach. Value > 0 = set that client; value == 0 = clear.
            // Null (default) leaves the field untouched so existing PUT callers
            // that don't know about UserId keep working unchanged.
            if (dto.UserId.HasValue)
            {
                if (dto.UserId.Value > 0)
                {
                    var clientExists = await _userManager.Users
                        .AsNoTracking()
                        .AnyAsync(u => u.Id == dto.UserId.Value, ct);
                    if (!clientExists)
                        throw new ArgumentException("Invalid UserId (client not found).");
                    e.UserId = dto.UserId.Value;
                }
                else
                {
                    e.UserId = null;
                }
            }
            // Validate Room/Set relationship only if either changed and both are present
            if ((roomChanged || setChanged) && e.RoomId.HasValue && e.SetId.HasValue)
            {
                var room = await _repoRoom.Query()
                    .Include(r => r.Sets)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == e.RoomId.Value, ct)
                    ?? throw new ArgumentException("Invalid RoomId.");

                if (!room.IsOpenSet)
                {
                    var belongs = room.Sets.Any(s => s.Id == e.SetId.Value);
                    if (!belongs) throw new ArgumentException("Selected SetId does not belong to the selected Room.");
                }
            }

            // Optional: prevent exact same (RoomId, SetId, StatusId) clash ONLY if caller changed StatusId
            // (mirrors your CreateGameSession check that included StatusId in the predicate)
            if (dto.StatusId.HasValue && e.RoomId.HasValue && e.SetId.HasValue)
            {
                var statusId = dto.StatusId.Value;
                var clash = await _repo.Query(true)
                    .AsNoTracking()
                    .AnyAsync(t =>
                        t.Id != e.Id &&
                        t.RoomId == e.RoomId &&
                        t.SetId == e.SetId &&
                        t.StatusId == statusId, ct);

                if (clash)
                    throw new InvalidOperationException("Another transaction with the same Room/Set and Status already exists.");
            }

            e.ModifiedOn = DateTime.UtcNow;
            await LogAuditAsync(
                transactionId: id,
                changedBy: updatedBy,
                action: "AdminUpdate",
                fieldChanged: string.Join(", ", changedFields),
                notes: "Manual admin update",
                ct: ct
            );
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> AttachClientAsync(int transactionId, int? userId, CancellationToken ct = default)
        {
            // Narrow, cashier-safe version of UpdateAsync. Only touches the
            // UserId column, so it doesn't need admin role on the controller.
            var e = await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (e is null) return false;

            var actor = _http?.HttpContext?.User?.Identity?.Name ?? "system";
            var oldValue = e.UserId?.ToString() ?? "(none)";

            if (userId.HasValue && userId.Value > 0)
            {
                var clientExists = await _userManager.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Id == userId.Value, ct);
                if (!clientExists)
                    throw new ArgumentException("Invalid UserId (client not found).");
                e.UserId = userId.Value;
            }
            else
            {
                e.UserId = null;
            }

            e.ModifiedOn = DateTime.UtcNow;

            await LogAuditAsync(
                transactionId: transactionId,
                changedBy: actor,
                action: "ClientAttached",
                fieldChanged: "UserId",
                oldValue: oldValue,
                newValue: e.UserId?.ToString() ?? "(detached)",
                notes: "Cashier attached/changed client on open session",
                ct: ct);

            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<BaseResponse<TransactionDto>> ReplaceTransactionItemsAsync(
            int transactionId,
            IReadOnlyList<(int itemId, int quantity)> lines,
            string actor,
            CancellationToken ct = default)
        {
            // ADMIN-ONLY editor. Deliberately has NO status / type checks —
            // the whole point is fixing mistakes on closed invoices. Auth
            // is enforced at the controller ([Authorize(Roles="admin")]).
            var tx = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)   // mapper needs Item.Name on the early-return path
                .Include(t => t.Discount)
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

            if (tx is null)
                return new BaseResponse<TransactionDto>(false, "Not found", "Transaction not found.");

            // Dedupe + validate incoming lines.
            var wanted = lines
                .Where(l => l.quantity > 0)
                .GroupBy(l => l.itemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.quantity));

            var allIds = wanted.Keys
                .Union(tx.TransactionItems.Select(ti => ti.ItemId))
                .Distinct().ToList();
            var dbItems = await _repoItem.Query(asNoTracking: false)
                .Where(i => allIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            var missing = wanted.Keys.Where(id => !dbItems.ContainsKey(id)).ToList();
            if (missing.Any())
                return new BaseResponse<TransactionDto>(false, "Invalid items",
                    $"Item IDs do not exist: {string.Join(", ", missing)}");

            // Diff current → wanted, computing per-item deltas.
            var current = tx.TransactionItems.ToDictionary(ti => ti.ItemId, ti => ti);
            var consumeDeltas = new List<(int itemId, decimal qty)>();  // to consume (positive)
            var restoreDeltas = new List<(int itemId, decimal qty)>();  // to restore (positive)
            decimal priceDelta = 0m;
            var changeLog = new List<string>();

            // Additions / quantity changes
            foreach (var (itemId, newQty) in wanted)
            {
                var item = dbItems[itemId];
                if (current.TryGetValue(itemId, out var line))
                {
                    var delta = newQty - line.Quantity;
                    if (delta == 0) continue;
                    changeLog.Add($"{item.Name}: {line.Quantity} → {newQty}");
                    line.Quantity = newQty;
                    if (delta > 0)
                    {
                        item.Quantity -= delta;                 // legacy counter
                        consumeDeltas.Add((itemId, delta));
                    }
                    else
                    {
                        item.Quantity += -delta;
                        restoreDeltas.Add((itemId, -delta));
                    }
                    priceDelta += item.Price * delta;
                }
                else
                {
                    changeLog.Add($"{item.Name}: added ×{newQty}");
                    var newLine = new TransactionItem
                    {
                        TransactionRecordId = tx.Id,
                        ItemId = itemId,
                        Quantity = newQty,
                    };
                    tx.TransactionItems.Add(newLine);
                    await _repoTrxItem.AddAsync(newLine, ct);
                    item.Quantity -= newQty;
                    consumeDeltas.Add((itemId, newQty));
                    priceDelta += item.Price * newQty;
                }
            }

            // Removals (present now, absent from the wanted list)
            foreach (var (itemId, line) in current)
            {
                if (wanted.ContainsKey(itemId)) continue;
                var name = dbItems.TryGetValue(itemId, out var item) ? item.Name : $"Item#{itemId}";
                changeLog.Add($"{name}: removed (was ×{line.Quantity})");
                if (item != null) item.Quantity += line.Quantity;
                restoreDeltas.Add((itemId, line.Quantity));
                priceDelta -= (item?.Price ?? 0m) * line.Quantity;
                _repoTrxItem.Remove(line);
            }

            if (changeLog.Count == 0)
                return new BaseResponse<TransactionDto>(true, null, "No changes.", _mapper.ToDto(tx));

            // Ingredient stock — symmetric consume/restore via recipes.
            // Log-and-continue on failure so a stock hiccup doesn't block
            // the financial correction (same policy as DeleteAsync).
            try
            {
                if (consumeDeltas.Count > 0)
                    await _stockService.ConsumeForOrderAsync(tx.Id, consumeDeltas, actor, ct);
                if (restoreDeltas.Count > 0)
                    await _stockService.RestoreForLinesAsync(tx.Id, restoreDeltas,
                        reason: $"Admin item edit on tx #{tx.Id}", actor: actor, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ingredient stock sync failed during admin item edit on Tx {TxId}; manual adjustment may be needed.",
                    tx.Id);
            }

            // TotalPrice: adjust by the net item delta, honoring an active
            // discount. Delta-based (not full recompute) so a game session's
            // time-based portion of the total is preserved untouched.
            var pct = tx.Discount != null && tx.Discount.IsActive ? tx.Discount.Percentage : 0;
            var effectiveDelta = pct is > 0 and < 100
                ? priceDelta * (1m - pct / 100m)
                : priceDelta;
            var oldTotal = tx.TotalPrice;
            tx.TotalPrice = Math.Max(0m, Math.Round(tx.TotalPrice + effectiveDelta, 2));
            tx.ModifiedOn = DateTime.UtcNow;

            await LogAuditAsync(
                transactionId: tx.Id,
                changedBy: actor,
                action: "AdminItemsEdit",
                fieldChanged: "TransactionItems",
                oldValue: $"Total={oldTotal:F2}",
                newValue: $"Total={tx.TotalPrice:F2}",
                notes: string.Join("; ", changeLog),
                ct: ct);

            await _uow.SaveChangesAsync(ct);

            // Reload with names for the response.
            var reloaded = await _repo.Query()
                .Include(t => t.Room).Include(t => t.Set).Include(t => t.Game)
                .Include(t => t.GameType).Include(t => t.GameSetting)
                .Include(t => t.Discount).Include(t => t.User)
                .Include(t => t.TransactionItems).ThenInclude(ti => ti.Item)
                .AsSplitQuery().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tx.Id, ct);

            return new BaseResponse<TransactionDto>(true, null,
                $"Items updated ({changeLog.Count} change(s)).",
                _mapper.ToDto(reloaded ?? tx));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (e is null) return false;

            var actor = _http?.HttpContext?.User?.Identity?.Name ?? "system";

            // If it's a coffee-shop order (no GameId), return stock — TWO
            // places need touching:
            //   1) Item.Quantity (the legacy per-item counter we still
            //      decrement on sale)
            //   2) Ingredient.QuantityOnHand via RestoreForOrderAsync —
            //      mirrors every Consumption StockMovement this tx wrote
            //      so the kitchen's actual stock reflects the void.
            if (e.GameId == null && e.TransactionItems?.Count > 0)
            {
                var itemIds = e.TransactionItems.Select(i => i.ItemId).Distinct().ToList();
                var dbItems = await _repoItem.Query(false)
                    .Where(i => itemIds.Contains(i.Id))
                    .ToListAsync(ct);

                foreach (var it in dbItems)
                {
                    var qty = e.TransactionItems.Where(x => x.ItemId == it.Id).Sum(x => x.Quantity);
                    it.Quantity += qty;
                }

                // Reverse all Consumption StockMovements for this tx.
                // No-op if there were none (non-recipe items, or older
                // transaction predating stock V1).
                try
                {
                    await _stockService.RestoreForOrderAsync(e.Id, actor, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Stock reversal failed for deleted transaction {Id}; continuing with delete to avoid leaving a half-state. Manual stock adjustment may be needed.",
                        e.Id);
                }
            }

            // Permanent record in AdminAuditLogs (no FK to TransactionRecord,
            // so this survives the cascade delete). TransactionAuditLog is
            // wired with OnDelete=Cascade so its rows die with the parent —
            // including any "Deleted" entry we'd add there. AdminAuditLog
            // keeps the receipt.
            var lineSummary = e.TransactionItems == null || e.TransactionItems.Count == 0
                ? "no items"
                : string.Join(", ",
                    e.TransactionItems.GroupBy(x => x.ItemId)
                        .Select(g => $"Item#{g.Key} x{g.Sum(x => x.Quantity)}"));
            await LogPermanentAdminAuditAsync(
                transactionId: id,
                entityName: $"Transaction #{id} (Total ${e.TotalPrice:F2})",
                action: "Deleted",
                changedBy: actor,
                summary: $"Total ${e.TotalPrice:F2}, status {e.StatusId}, items: {lineSummary}",
                payload: new
                {
                    e.Id,
                    e.TotalPrice,
                    e.StatusId,
                    e.GameId,
                    e.RoomId,
                    Items = e.TransactionItems?.Select(ti => new { ti.ItemId, ti.Quantity })
                },
                ct: ct);

            // Cancel any scheduled job if you persist it
            // if (!string.IsNullOrEmpty(e.HangfireJobId)) BackgroundJob.Delete(e.HangfireJobId);

            // FK on TransactionItems is RESTRICT -> remove children first
            if (e.TransactionItems is not null && e.TransactionItems.Count > 0)
                _repoTrxItem.RemoveRange(e.TransactionItems);

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public string GetReqId()
        {
            var ctx = _http.HttpContext;
            if (ctx is null) return Guid.NewGuid().ToString("N");

            if (ctx.Request.Headers.TryGetValue("X-Request-ID", out var v) && !StringValues.IsNullOrEmpty(v))
                return v.ToString();

            return ctx.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        }

        private static string HashObject(object o)
        {
            var json = JsonSerializer.Serialize(o);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes);
        }

        private static string ItemsSignature(IEnumerable<OrderItemRequest> items)
        {
            // stable order -> (ItemId:Qty;...)
            var parts = items
                .OrderBy(i => i.ItemId)
                .ThenBy(i => i.Quantity)
                .Select(i => $"{i.ItemId}:{i.Quantity}");
            return string.Join(';', parts);
        }

        private static (string Provider, string Code) ExtractDbCode(Exception ex)
        {
            // Helps identify transient/unique violations/etc.
            if (ex is DbUpdateException dbe) ex = dbe.InnerException ?? ex;

            if (ex is PostgresException pg)
                return ("PG", pg.SqlState ?? "");

            return ("-", "-");
        }

        private async Task<int?> GetOrCreateClientUserIdByPhoneAsync(string? phoneNumber, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return null;

            var phone = phoneNumber.Trim();

            // 1) Try to find existing user
            var existing = await _userManager.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);

            if (existing != null)
                return existing.Id;

            // 2) Create a new minimal client user
            var user = new AppUser
            {
                UserName = phone,
                PhoneNumber = phone,
                DisplayName = phone,
                StatusId = (int)UserStatus.Active
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                // if creation failed, we just don't link UserId (keep it null)
                // you can log errors here if you want
                return null;
            }

            // optionally assign Client role if you use it
            await _userManager.AddToRoleAsync(user, "Client");

            return user.Id;
        }

        public async Task<BaseResponse<TransactionDto>> CloseGameSession(int invoiceId, string updatedBy, CancellationToken ct = default)
        {
            var reqId = GetReqId();
            var sig = HashObject(new { invoiceId });

            // 1) Load transaction (unpaid session)
            var tx = await _repo.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            if (tx is null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice", "The specified invoice/transaction does not exist.");

            if (tx.StatusId != 7)
                return new BaseResponse<TransactionDto>(false, "Invalid status", "This session is not in 'processed & unpaid' status.");

            // 2) Load game setting
            var setting = await _repoSetting.Query()
                .AsNoTracking()
                .Where(s => s.Id == tx.GameSettingId)
                .Select(s => new { s.Price })
                .FirstOrDefaultAsync(ct);

            if (setting is null)
                return new BaseResponse<TransactionDto>(false, "Invalid game setting", "The game setting linked to this session does not exist.");

            // 3) Calculate played time
            var nowUtc = DateTime.UtcNow;
            var startedOn = tx.CreatedOn.AddMinutes(5);
            if (startedOn == default)
                return new BaseResponse<TransactionDto>(false, "Invalid data", "Session start time is missing.");

            var totalMinutes = (nowUtc - startedOn).TotalMinutes;
            if (totalMinutes < 1)
                totalMinutes = 1;

            // RAW hours
            decimal rawHours = (decimal)(totalMinutes / 60.0);

            // 4) Players
            var persons = tx.numberOfPersons > 0 ? tx.numberOfPersons : 1;

            // 5) Rounding logic — Rami's rule (2026-07):
            //
            //   Board games (GameTypeId == 2):
            //     0 to 60 min  → 1.0 hour
            //     61 to 90 min → 1.5 hours
            //     91+ min      → DAY PASS (flat, no hours)
            //
            //   PS5 (GameTypeId == 6):
            //     0 to 60 min   → 1.0 hour
            //     61 to 90 min  → 1.5 hours
            //     91 to 120 min → 2.0 hours
            //     121 to 150 min→ 2.5 hours
            //     151 to 180 min→ 3.0 hours
            //     ... same 30-min snap indefinitely.
            //
            // The 5-minute grace at line 1204 (startedOn = CreatedOn+5min)
            // means a customer who leaves in the first 5 minutes racks up
            // ~0 real minutes here, but Rami's rule says "0 to 1 = 1 hour"
            // so even a 1-min stay bills 1 full hour. That's intentional.
            bool isBoardGame = tx.GameTypeId == 2;

            // Board game half-hour snap up to 90 min; anything beyond → day pass.
            static decimal GetBilledHoursBoardGame(double minutes)
            {
                if (minutes <= 0) return 0m;
                if (minutes <= 60) return 1.0m;
                return 1.5m;    // 61-90 min (91+ is caught by the day-pass branch below)
            }

            // PS5: 60 → 1.0, then every 30 min adds 0.5 hour. Ceiling on the
            // 30-min block starting AFTER the first hour so 61-90 = 1.5,
            // 91-120 = 2.0, 121-150 = 2.5, 151-180 = 3.0, ...
            static decimal GetBilledHoursPs5(double minutes)
            {
                if (minutes <= 0) return 0m;
                if (minutes <= 60) return 1.0m;
                var overrun = minutes - 60.0;                // minutes past the first hour
                var extraHalfHours = (int)Math.Ceiling(overrun / 30.0);
                return 1.0m + 0.5m * extraHalfHours;
            }

            // Board game day-pass kicks in the moment we cross 1h30 (91 min).
            decimal totalPriceBeforeDiscount;
            decimal billedHours;
            if (isBoardGame && totalMinutes > 90)
            {
                var dayPass = await _repoSetting.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.IsDayPass == true && s.GameId == tx.GameId, ct);

                if (dayPass != null && dayPass.Price > 0)
                {
                    // Flat day-pass. Hours field on the receipt still needs
                    // a value — show actual runtime rounded up to whole
                    // hours so it's informative (e.g. "3 hours") even
                    // though the price is flat.
                    billedHours = Math.Ceiling((decimal)(totalMinutes / 60.0));
                    if (billedHours < 1m) billedHours = 1m;
                    totalPriceBeforeDiscount = dayPass.Price * persons;
                }
                else
                {
                    // Configuration gap: no day-pass setting exists for
                    // this game. Fall back to the PS5-style snap so we
                    // still charge something rather than crash.
                    billedHours = GetBilledHoursPs5(totalMinutes);
                    totalPriceBeforeDiscount = setting.Price * billedHours * persons;
                }
            }
            else if (isBoardGame)
            {
                // 0-90 min board-game window
                billedHours = GetBilledHoursBoardGame(totalMinutes);
                totalPriceBeforeDiscount = setting.Price * billedHours * persons;
            }
            else
            {
                // PS5 (or any non-board category) — infinite half-hour snap.
                billedHours = GetBilledHoursPs5(totalMinutes);
                totalPriceBeforeDiscount = setting.Price * billedHours * persons;
            }

            // ❌ NO FINAL PRICE ROUNDING
            decimal totalPrice = totalPriceBeforeDiscount;

            // 7) Discounts
            if (tx.DiscountId.HasValue && tx.DiscountId.Value != 0)
            {
                var discount = await _repoDiscount.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == tx.DiscountId.Value, ct);

                if (discount is not null && discount.IsActive)
                {
                    totalPrice -= (totalPrice * discount.Percentage / 100m);
                    if (totalPrice < 0)
                        totalPrice = 0;
                }
            }

            // 8) Update DB
            var tracked = await _repo.GetByIdAsync(invoiceId, asNoTracking: false, ct);
            if (tracked is null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice", "The specified invoice/transaction does not exist.");

            tracked.Hours = billedHours;
            tracked.TotalPrice = totalPrice;
            tracked.StatusId = 6;
            tracked.ModifiedOn = nowUtc;
            tracked.CreatedBy = updatedBy ?? tracked.CreatedBy;

            // Make set available
            if (tx.SetId.HasValue)
            {
                var set = await _repoSet.Query(asNoTracking: false)
                    .FirstOrDefaultAsync(s => s.Id == tx.SetId.Value, ct);

                if (set != null)
                    set.StatusId = 9;
            }
            await LogAuditAsync(
                    transactionId: invoiceId,
                    changedBy: updatedBy ?? "system",
                    action: "CloseGameSession",
                    fieldChanged: "StatusId",
                    oldValue: "7",
                    newValue: "6",
                    notes: $"BilledHours={billedHours}, TotalPrice={totalPrice:F2}",
                    ct: ct
                );

            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch
            {
                return new BaseResponse<TransactionDto>(false, "db error", "Failed to close the session.");
            }
            // ========================================
            // ✅ CALCULATE LOYALTY TICKETS
            // ========================================
            if (tracked.UserId.HasValue)
            {
                try
                {
                    var userPhone = await GetUserPhoneNumberAsync(tracked.UserId.Value, ct);
                    var userName = await GetUserFullNameAsync(tracked.UserId.Value, ct);

                    if (!string.IsNullOrWhiteSpace(userPhone) && await IsClientUserAsync(tracked.UserId.Value, ct))
                    {
                        var loyaltyRequest = new CalculateTicketsRequest
                        {
                            TransactionId = tracked.Id,
                            TotalAmount = totalPrice,
                            CustomerPhone = userPhone,
                            CustomerName = userName ?? updatedBy
                        };

                        var loyaltyResponse = await _loyaltyService.CalculateAndAssignTicketsAsync(loyaltyRequest);

                        if (loyaltyResponse.Success)
                        {
                            _logger.LogInformation(
                                "✅ Loyalty tickets assigned on close: TxId={TxId}, User={UserId}, Phone={Phone}, Tickets={Tickets}, Balance=${Balance:F2}",
                                tracked.Id, tracked.UserId.Value, userPhone, loyaltyResponse.TicketsEarned, loyaltyResponse.PendingBalance);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "⚠️ Loyalty calculation failed on close: TxId={TxId}, User={UserId}, Reason={Message}",
                                tracked.Id, tracked.UserId.Value, loyaltyResponse.Message);
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "ℹ️ No phone number for loyalty on close: TxId={TxId}, User={UserId}",
                            tracked.Id, tracked.UserId.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "❌ Error calculating loyalty tickets on close: TxId={TxId}, User={UserId}",
                        tracked.Id, tracked.UserId.Value);
                }
            }
            // ========================================


            // Reload with navigations so the receipt gets room/set/game
            // names AND the attached client's name (CR#2: customer name on
            // the printed receipt). `tracked` was loaded without includes,
            // so mapping it directly would print userName as blank.
            var closed = await _repo.Query()
                .Include(t => t.Room)
                .Include(t => t.Set)
                .Include(t => t.Game)
                .Include(t => t.GameType)
                .Include(t => t.GameSetting)
                .Include(t => t.Discount)
                .Include(t => t.User)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            var dto = _mapper.ToDto(closed ?? tracked);
            try
            {
                var journalResult = await _journalService.CreateJournalEntryFromTransactionAsync(
                    dto.Id,
                    ct);

                if (journalResult.Success)
                {
                    _logger.LogInformation(
                        "Journal entry {EntryNumber} created for closed session {TxId}",
                        journalResult.Data?.EntryNumber,
                        dto.Id);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to create journal entry for closed session {TxId}: {Error}",
                        dto.Id,
                        journalResult.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Exception creating journal entry for closed session {TxId}",
                    dto.Id);
                // Don't fail the session close, just log the error
            }

            return new BaseResponse<TransactionDto>(true, null, "Game session closed successfully.", dto);
        }


        public async Task<BaseResponse<List<TransactionDto>>> GetOpenBoardGameSessions(CancellationToken ct = default)
        {
            return await GetOpenSessionsByCategoryAsync(2, ct); // 2 = board games
        }

        public async Task<BaseResponse<List<TransactionDto>>> GetOpenPs5Sessions(CancellationToken ct = default)
        {
            return await GetOpenSessionsByCategoryAsync(6, ct); // 5 = PS5
        }

        private async Task<BaseResponse<List<TransactionDto>>> GetOpenSessionsByCategoryAsync(
            int categoryId,
            CancellationToken ct = default)
        {
            var reqId = GetReqId();

            var query = _repo.Query()
                .AsNoTracking()
                .Where(t => t.StatusId == 7 && t.GameTypeId == categoryId)
                .Include(t => t.Room)
                .Include(t => t.Set)
                .Include(t => t.Game)
                .Include(t => t.GameType)
                .Include(t => t.GameSetting)
                // Client attached via /transactions/{id}/client. Without
                // this Include the mapper sees e.User == null and returns
                // userName: null, so the card blanks out on every refresh.
                .Include(t => t.User)
                .AsSplitQuery()
                .OrderByDescending(t => t.CreatedOn);

            var entities = await query.ToListAsync(ct);

            var dtos = new List<TransactionDto>();
            foreach (var e in entities)
            {
                dtos.Add(_mapper.ToDto(e));
            }

            _logger.LogInformation(
                "GS/GetOpenSessions ReqId={ReqId} Category={CategoryId} Count={Count}",
                reqId, categoryId, dtos.Count);

            return new BaseResponse<List<TransactionDto>>(
                true,
                null,
                "Open sessions retrieved successfully.",
                dtos);
        }

        /// <summary>
        /// Get user's phone number from Identity system
        /// </summary>
        private async Task<string?> GetUserPhoneNumberAsync(int userId, CancellationToken ct)
        {
            try
            {
                // Find user by ID
                var user = await _userManager.FindByIdAsync(userId.ToString());

                if (user == null)
                {
                    _logger.LogWarning("User not found for loyalty tickets: UserId={UserId}", userId);
                    return null;
                }

                // Get phone number
                var phoneNumber = await _userManager.GetPhoneNumberAsync(user);

                if (string.IsNullOrWhiteSpace(phoneNumber))
                {
                    _logger.LogWarning("User has no phone number for loyalty tickets: UserId={UserId}", userId);
                    return null;
                }

                return phoneNumber;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting phone number for user {UserId}", userId);
                return null;
            }
        }

        /// <summary>
        /// Get user's full name from Identity system (optional - for better customer records)
        /// </summary>
        private async Task<string?> GetUserFullNameAsync(int userId, CancellationToken ct)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());

                if (user == null)
                    return null;

                // Assuming your ApplicationUser has FirstName and LastName properties
                // Adjust based on your actual User entity structure
                return $"{user.FirstName} {user.LastName}".Trim();

                // OR if you just have a Name property:
                // return user.Name;

                // OR if you want to use UserName as fallback:
                // return user.Name ?? user.UserName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user name for user {UserId}", userId);
                return null;
            }
        }
        private async Task<bool> IsClientUserAsync(int userId, CancellationToken ct)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                    return false;

                var roles = await _userManager.GetRolesAsync(user);

                return roles.Any(r => r.Equals("client", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking role for user {UserId}", userId);
                return false;
            }
        }
        public async Task<BaseResponse<List<TransactionDto>>> GetOpenFnbInvoices(CancellationToken ct = default)
        {
            var reqId = GetReqId();

            var query = _repo.Query()
                .AsNoTracking()
                .Where(t => t.StatusId == 7 && t.GameId == null)  // Open invoices for FNB only
                .Include(t => t.Room)
                .Include(t => t.Game)
                .Include(t => t.GameType)
                .Include(t => t.GameSetting)
                .Include(t => t.Discount)
                .Include(t => t.Set)
                .Include(t => t.User)  // IMPORTANT: Include User for username
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                .OrderByDescending(t => t.CreatedOn);

            var entities = await query.ToListAsync(ct);

            var dtos = new List<TransactionDto>();
            foreach (var e in entities)
            {
                dtos.Add(_mapper.ToDto(e));
            }

            _logger.LogInformation(
                "FNB/GetOpenInvoices ReqId={ReqId} Count={Count}",
                reqId, dtos.Count);

            return new BaseResponse<List<TransactionDto>>(
                true,
                null,
                "Open FNB invoices retrieved successfully.",
                dtos);
        }

        public async Task<BaseResponse<TransactionDto>> AddItemsToOpenInvoice(int invoiceId, List<OrderItemRequest> itemsRequest, string updatedBy, CancellationToken ct)
        {
            var reqId = GetReqId();
            var sig = itemsRequest is null ? "-" : ItemsSignature(itemsRequest);

            if (itemsRequest is null || itemsRequest.Count == 0)
                return new BaseResponse<TransactionDto>(false, "No items", "No items provided.");

            // 1) Load the transaction (must be open FNB invoice)
            var trx = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems)
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            if (trx is null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice",
                    "The specified invoice does not exist.");

            // Must be open (Status=7) and FNB (GameId=null)
            if (trx.StatusId != 7)
                return new BaseResponse<TransactionDto>(false, "Invoice closed",
                    "This invoice is already closed. Cannot add items.");

            if (trx.GameId != null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice type",
                    "Cannot add items to game invoices.");

            // 2) Validate items
            var requested = itemsRequest
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var invalidQty = requested.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList();
            if (invalidQty.Any())
                return new BaseResponse<TransactionDto>(false, "Invalid quantity",
                    $"Invalid quantity (<=0) for items: {string.Join(", ", invalidQty)}");

            var ids = requested.Keys.ToList();

            var dbItems = await _repoItem.Query(false)
                .Where(i => ids.Contains(i.Id))
                .ToListAsync(ct);

            if (dbItems.Count != ids.Count)
            {
                var missing = ids.Except(dbItems.Select(i => i.Id)).ToList();
                return new BaseResponse<TransactionDto>(false, "Invalid items",
                    $"The following item IDs do not exist: {string.Join(", ", missing)}");
            }

            // Stock check — skip recipe items (their real stock is on
            // Ingredient.QuantityOnHand, not the Item.Quantity counter).
            var recipeItemIds = new HashSet<int>(
                await _repoRecipeLine.Query()
                    .Where(r => ids.Contains(r.ItemId))
                    .Select(r => r.ItemId)
                    .Distinct()
                    .ToListAsync(ct));

            var outOfStock = new List<string>();
            foreach (var it in dbItems)
            {
                if (recipeItemIds.Contains(it.Id)) continue;
                var need = requested[it.Id];
                if (it.Quantity < need)
                    outOfStock.Add($"{it.Name} (needs {need}, has {it.Quantity})");
            }
            if (outOfStock.Any())
                return new BaseResponse<TransactionDto>(false, "Out of stock",
                    $"The following items are out of stock: {string.Join("; ", outOfStock)}");

            // 3) Add new items to transaction
            var newTrxItems = new List<TransactionItem>();
            decimal additionalTotal = 0m;

            foreach (var it in dbItems)
            {
                var qty = requested[it.Id];

                // Check if item already exists in transaction
                var existing = trx.TransactionItems.FirstOrDefault(ti => ti.ItemId == it.Id);
                if (existing != null)
                {
                    // Update quantity
                    existing.Quantity += qty;
                }
                else
                {
                    // Add new item
                    var newItem = new TransactionItem
                    {
                        TransactionRecordId = trx.Id,
                        ItemId = it.Id,
                        Quantity = qty,
                    };
                    trx.TransactionItems.Add(newItem);
                    await _repoTrxItem.AddAsync(newItem, ct);
                }

                // Deduct stock
                it.Quantity -= qty;
                additionalTotal += (it.Price * qty);
            }

            // 4) Recalculate total (including existing discount if any)
            trx.TotalPrice += additionalTotal;

            // Reapply discount if one exists
            if (trx.DiscountId.HasValue)
            {
                var discount = await _repoDiscount.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == trx.DiscountId.Value, ct);

                if (discount != null && discount.IsActive)
                {
                    // Recalculate from subtotal
                    decimal subtotal = 0m;
                    foreach (var ti in trx.TransactionItems)
                    {
                        var item = await _repoItem.Query()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(i => i.Id == ti.ItemId, ct);
                        if (item != null)
                            subtotal += (item.Price * ti.Quantity);
                    }

                    trx.TotalPrice = subtotal - (subtotal * discount.Percentage / 100);
                    if (trx.TotalPrice < 0) trx.TotalPrice = 0;
                }
            }

            trx.ModifiedOn = DateTime.UtcNow;
            trx.CreatedBy = updatedBy ?? trx.CreatedBy;  // Track who added items

            await LogAuditAsync(
                transactionId: invoiceId,
                changedBy: updatedBy ?? "system",
                action: "AddItems",
                fieldChanged: "TransactionItems",
                notes: $"Added {itemsRequest.Count} item(s). NewTotal={trx.TotalPrice:F2}",
                ct: ct
            );

            // Consume ingredient stock for the newly added lines via the
            // recipe — matches the create-order path. We pass only the
            // delta (the qty being added now), NOT the line's full new qty,
            // because earlier consumption already accounted for the
            // pre-existing portion.
            try
            {
                var consumeLines = requested.Select(kv => (kv.Key, (decimal)kv.Value)).ToList();
                await _stockService.ConsumeForOrderAsync(invoiceId, consumeLines, updatedBy, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Stock consumption failed when adding items to invoice {InvoiceId}; line was added but ingredient stock NOT decremented. Manual adjustment may be needed.",
                    invoiceId);
            }

            try
            {
                _logger.LogInformation(
                    "FNB/AddItems BEFORE_SAVE ReqId={ReqId} InvoiceId={InvoiceId} NewItems={Count} NewTotal={Total}",
                    reqId, invoiceId, itemsRequest.Count, trx.TotalPrice);

                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var (prov, code) = ExtractDbCode(ex);
                _logger.LogError(ex,
                    "FNB/AddItems ERROR ReqId={ReqId} DB={Prov}:{Code} InvoiceId={InvoiceId} Sig={Sig}",
                    reqId, prov, code, invoiceId, sig);

                return new BaseResponse<TransactionDto>(false, "db error",
                    "Failed to add items to invoice. Please try again.");
            }
            // IMPORTANT: Reload with all includes
            var reloaded = await _repo.Query()
                .AsNoTracking()
                .Include(t => t.Room)
                .Include(t => t.Game)
                .Include(t => t.GameType)
                .Include(t => t.GameSetting)
                .Include(t => t.Discount)
                .Include(t => t.Set)
                .Include(t => t.User)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                        .ThenInclude(i => i.CoffeeShopOrders)  // Include if needed
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            if (reloaded == null)
                return new BaseResponse<TransactionDto>(false, "error",
                    "Invoice closed but could not reload.");


            var dto = _mapper.ToDto(reloaded);
            return new BaseResponse<TransactionDto>(true, null,
                "Items added to invoice successfully.", dto);
        }

        public async Task<BaseResponse<TransactionDto>> CloseOpenInvoice(int invoiceId, string updatedBy, CancellationToken ct)
        {
            var reqId = GetReqId();

            // Load transaction (must be open FNB invoice)
            var trx = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                .Include(t => t.Discount)
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            if (trx is null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice",
                    "The specified invoice does not exist.");

            // Must be open (Status=7)
            if (trx.StatusId != 7)
                return new BaseResponse<TransactionDto>(false, "Already closed",
                    "This invoice is already closed.");

            // Must be FNB invoice (GameId=null)
            if (trx.GameId != null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice type",
                    "Use CloseGameSession for game invoices.");

            // Ensure there are items
            if (trx.TransactionItems == null || trx.TransactionItems.Count == 0)
                return new BaseResponse<TransactionDto>(false, "Empty invoice",
                    "Cannot close an invoice with no items.");

            // Close the invoice
            trx.StatusId = 6;  // Closed/Paid
            trx.ModifiedOn = DateTime.UtcNow;
            trx.CreatedBy = updatedBy ?? trx.CreatedBy;

            await LogAuditAsync(
                transactionId: invoiceId,
                changedBy: updatedBy ?? "system",
                action: "CloseInvoice",
                fieldChanged: "StatusId",
                oldValue: "7",
                newValue: "6",
                notes: $"FNB invoice closed. Total={trx.TotalPrice:F2}",
                ct: ct
            );

            try
            {
                _logger.LogInformation(
                    "FNB/CloseInvoice BEFORE_SAVE ReqId={ReqId} InvoiceId={InvoiceId} Total={Total}",
                    reqId, invoiceId, trx.TotalPrice);

                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var (prov, code) = ExtractDbCode(ex);
                _logger.LogError(ex,
                    "FNB/CloseInvoice ERROR ReqId={ReqId} DB={Prov}:{Code} InvoiceId={InvoiceId}",
                    reqId, prov, code, invoiceId);

                return new BaseResponse<TransactionDto>(false, "db error",
                    "Failed to close invoice. Please try again.");
            }

            string userName = "";
            if (trx.UserId != null && trx.UserId > 0)
            {
                try
                {
                    var userPhone = await GetUserPhoneNumberAsync(trx.UserId.Value, ct);
                    userName = await GetUserFullNameAsync(trx.UserId.Value, ct);

                    if (!string.IsNullOrWhiteSpace(userPhone) && await IsClientUserAsync(trx.UserId.Value, ct))
                    {
                        var loyaltyRequest = new CalculateTicketsRequest
                        {
                            TransactionId = trx.Id,
                            TotalAmount = trx.TotalPrice,
                            CustomerPhone = userPhone,
                            CustomerName = userName ?? ""
                        };

                        var loyaltyResponse = await _loyaltyService.CalculateAndAssignTicketsAsync(loyaltyRequest);

                        if (loyaltyResponse.Success)
                        {
                            _logger.LogInformation(
                                "✅ Loyalty tickets assigned: TxId={TxId}, User={UserId}, Phone={Phone}, Tickets={Tickets}, Balance=${Balance:F2}",
                                trx.Id, trx.UserId, userPhone, loyaltyResponse.TicketsEarned, loyaltyResponse.PendingBalance);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "⚠️ Loyalty calculation failed: TxId={TxId}, User={UserId}, Reason={Message}",
                                trx.Id, trx.UserId, loyaltyResponse.Message);
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "ℹ️ No phone number for loyalty: TxId={TxId}, User={UserId}",
                            trx.Id, trx.UserId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "❌ Error calculating loyalty tickets: TxId={TxId}, User={UserId}",
                        trx.Id, trx.UserId);
                }
            }

            var dto = _mapper.ToDto(trx);
            return new BaseResponse<TransactionDto>(true, null,
                "Invoice closed successfully.", dto);
        }

        public async Task<BaseResponse<TransactionDto>> UpdateOpenInvoiceSet(int invoiceId, int? setId, string updatedBy, CancellationToken ct)
        {
            var reqId = GetReqId();

            var trx = await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            if (trx is null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice",
                    "The specified invoice does not exist.");

            if (trx.StatusId != 7)
                return new BaseResponse<TransactionDto>(false, "Invoice closed",
                    "Cannot update set for closed invoices.");

            trx.SetId = setId;
            trx.ModifiedOn = DateTime.UtcNow;
            trx.CreatedBy = updatedBy ?? trx.CreatedBy;

            await LogAuditAsync(
                transactionId: invoiceId,
                changedBy: updatedBy ?? "system",
                action: "UpdateSet",
                fieldChanged: "SetId",
                newValue: setId?.ToString() ?? "null",
                notes: "Set assignment updated on open invoice",
                ct: ct
            );
            
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var (prov, code) = ExtractDbCode(ex);
                _logger.LogError(ex,
                    "FNB/UpdateSet ERROR ReqId={ReqId} DB={Prov}:{Code} InvoiceId={InvoiceId}",
                    reqId, prov, code, invoiceId);

                return new BaseResponse<TransactionDto>(false, "db error",
                    "Failed to update set. Please try again.");
            }

            // Reload with includes
            var reloaded = await _repo.Query()
                .AsNoTracking()
                .Include(t => t.Set)
                .Include(t => t.User)
                .Include(t => t.Discount)
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                .FirstOrDefaultAsync(t => t.Id == invoiceId, ct);

            if (reloaded == null)
                return new BaseResponse<TransactionDto>(false, "error",
                    "Set updated but could not reload.");

            var dto = _mapper.ToDto(reloaded);
            return new BaseResponse<TransactionDto>(true, null,
                "Set updated successfully.", dto);
        }

        private async Task CreateKitchenBarOrdersAsync(TransactionRecord trx, List<TransactionItem> trxItems, string createdBy, string? tableNumber = null, string? guestName = null, CancellationToken ct = default)
        {
            var kitchenBarOrders = new List<KitchenBarOrder>();
            var now = DateTime.UtcNow;

            foreach (var trxItem in trxItems)
            {
                var item = trxItem.Item;
                if (item?.Category == null) continue;

                string? station = item.Category.ItemType switch
                {
                    "Food" => "Kitchen",
                    "Drinks" => "Bar",
                    "Tobacco" => "Bar",
                    _ => null
                };

                if (station != null)
                {
                    kitchenBarOrders.Add(new KitchenBarOrder
                    {
                        TransactionId = trx.Id,
                        TransactionItemId = trxItem.ItemId,
                        ItemId = item.Id,
                        Station = station,
                        Status = "Pending",
                        OrderedAt = now,
                        TableNumber = tableNumber,
                        GuestName = guestName,
                        ItemComment = null,
                        Quantity = trxItem.Quantity,
                        ItemName = item.Name,
                        ItemPrice = item.Price,
                        CreatedByUsername = createdBy,
                        CreatedAt = now
                    });
                }
            }

            if (kitchenBarOrders.Any())
            {
                await _repoKitchenBar.AddRangeAsync(kitchenBarOrders, ct);
                await _uow.SaveChangesAsync(ct); // Save first to get IDs

                _logger.LogInformation(
                    "Created {Count} kitchen/bar orders for Transaction {TrxId}",
                    kitchenBarOrders.Count, trx.Id);

                // NEW: Send SignalR notifications to Kitchen and Bar
                foreach (var order in kitchenBarOrders)
                {
                    var orderDto = new KitchenBarOrderDto(
                        order.Id,
                        order.TransactionId,
                        order.ItemId,
                        order.ItemName,
                        order.Quantity,
                        order.ItemPrice,
                        order.Station,
                        order.Status,
                        order.OrderedAt,
                        null,
                        null,
                        null,
                        null,
                        order.TableNumber,
                        order.GuestName,
                        order.ItemComment,
                        order.CreatedByUsername,
                        order.CreatedAt
                    );

                    await _hubContext.Clients.Group(order.Station)
                        .SendAsync("NewOrder", orderDto, ct);
                }
            }
        }

        private async Task LogAuditAsync(int transactionId, string changedBy, string action, string? fieldChanged = null, string? oldValue = null, string? newValue = null, string? notes = null,
            CancellationToken ct = default)
        {
            var log = new TransactionAuditLog
            {
                TransactionId = transactionId,
                ChangedBy = changedBy ?? "system",
                ChangedOn = DateTime.UtcNow,
                Action = action,
                FieldChanged = fieldChanged,
                OldValue = oldValue,
                NewValue = newValue,
                Notes = notes
            };
            await _repoAuditLog.AddAsync(log, ct);
            // Caller must call SaveChangesAsync — log is batched with the main save
        }

        /// <summary>
        /// Writes a row to the permanent AdminAuditLogs table. Use this for
        /// transaction deletes — TransactionAuditLog cascade-deletes with
        /// its parent, so without this we lose the "who deleted invoice #X"
        /// history the moment the delete commits.
        /// </summary>
        private async Task LogPermanentAdminAuditAsync(
            int transactionId,
            string entityName,
            string action,                  // 'Deleted' | 'LineRemoved' | 'ItemsAdded' | ...
            string changedBy,
            string? summary,
            object? payload,
            CancellationToken ct = default)
        {
            await _repoAdminAuditLog.AddAsync(new AdminAuditLog
            {
                EntityType = "TransactionRecord",
                EntityId = transactionId,
                EntityName = entityName.Length > 300 ? entityName[..300] : entityName,
                Action = action,
                FieldChanges = payload == null ? null : JsonSerializer.Serialize(payload),
                Snapshot = summary,
                ChangedBy = changedBy ?? "system",
                ChangedOn = DateTime.UtcNow,
            }, ct);
            // Caller saves.
        }

        // ===========================================================
        // Main dashboard: transactions filtered by created date + channel.
        // Used by the new "Transactions" card on the home dashboard, with
        // an .xlsx export endpoint sharing the same query path.
        // ===========================================================
        public async Task<PaginatedResponse<DashboardTransactionRowDto>> GetDashboardTransactionsAsync(
            DashboardTransactionsFilterDto filter,
            CancellationToken ct = default)
        {
            // CreatedOn-based filter to match the rest of the dashboards.
            var fromInclusive = filter.From?.Date;
            var toExclusive = filter.To?.Date.AddDays(1);

            var q = _repo.Query();

            if (fromInclusive.HasValue)
                q = q.Where(t => t.CreatedOn >= fromInclusive.Value);
            if (toExclusive.HasValue)
                q = q.Where(t => t.CreatedOn < toExclusive.Value);
            if (filter.ChannelId.HasValue)
                q = q.Where(t => t.ChannelId == filter.ChannelId.Value);

            // Total count first so the FE can render pagination.
            var totalCount = await q.CountAsync(ct);

            // Paginate at the DB. Pass Page=null + PageSize=null from the
            // controller for the export endpoint to skip paging entirely.
            var page = filter.Page ?? 1;
            var pageSize = filter.PageSize ?? 20;
            var unpaged = !filter.Page.HasValue && !filter.PageSize.HasValue;

            var ordered = q.OrderByDescending(t => t.CreatedOn).ThenByDescending(t => t.Id);
            IQueryable<TransactionRecord> windowed = ordered;
            if (!unpaged)
                windowed = ordered.Skip((page - 1) * pageSize).Take(pageSize);

            var rows = await windowed
                .Select(t => new DashboardTransactionRowDto(
                    t.Id,
                    t.CreatedOn,
                    t.CreatedBy,
                    t.StatusId,
                    t.TotalPrice,
                    t.ChannelId,
                    t.Channel != null ? t.Channel.Name : null,
                    t.Comment,
                    t.TransactionItems.Count
                ))
                .ToListAsync(ct);

            return new PaginatedResponse<DashboardTransactionRowDto>(
                totalCount, rows, page, pageSize);
        }

    }
}