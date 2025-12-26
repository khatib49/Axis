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
        private readonly UserManager<AppUser> _userManager;
        private readonly ILoyaltyService _loyaltyService;
        public TransactionRecordService(IBaseRepository<TransactionRecord> repo, IBaseRepository<Setting> repoSetting,
            IBaseRepository<Room> repoRoom, IBaseRepository<Game> repoGame, IBaseRepository<Item> repoItem,
            IBaseRepository<TransactionItem> repoTrxItem, IBaseRepository<Status> repoStatus, UserManager<AppUser> userManager,
            IBaseRepository<Discount> repoDiscount, IBaseRepository<Set> repoSet, ILoyaltyService loyaltyService,
        IUnitOfWork uow, DomainMapper mapper, ILogger<TransactionRecordService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _loyaltyService = loyaltyService;
            _repo = repo; _uow = uow; _mapper = mapper;
            _userManager = userManager;
            _repoSetting = repoSetting;
            _repoRoom = repoRoom;
            _repoGame = repoGame;
            _repoItem = repoItem;
            _repoTrxItem = repoTrxItem;
            _repoStatus = repoStatus;
            _repoDiscount = repoDiscount;
            _repoSet = repoSet;
            _logger = logger;
            _http = httpContextAccessor;
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
                null
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
                .Select(s => new { s.Hours, s.Price, s.IsOpenHour , s.IsDayPass })
                .FirstOrDefaultAsync(ct);
            
            if(setting is null)
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
            int statusToUse = (game.CategoryId == 2 || game.CategoryId== 6) || isDayPass ? 7 : 6; // 5: processed and unpaid, 6: processed and paid
           if(setting.IsDayPass == true)
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
                    DiscountId: discount?.Id ,
                    numberOfPersons: numberOfPersons,
                    Comment: comment
                );

                var e = _mapper.ToEntity(createDto);
                if (e.SetId == 0)
                    e.SetId = null;

            //e.ExpectedEndOn = expectedEndOn;

            try
            {
                _logger.LogInformation("GS/Session BEFORE_SAVE ReqId={ReqId} Room={RoomId} Set={SetId} Total={Total}",
                    reqId, e.RoomId, e.SetId, e.TotalPrice);
                await _repo.AddAsync(e, ct);
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

                        if (loyaltyResponse.Success)
                        {
                            _logger.LogInformation(
                                "✅ Loyalty tickets assigned: TxId={TxId}, User={UserId}, Phone={Phone}, Tickets={Tickets}, Balance=${Balance:F2}",
                                e.Id, userId.Value, userPhone, loyaltyResponse.TicketsEarned, loyaltyResponse.PendingBalance);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "⚠️ Loyalty calculation failed: TxId={TxId}, User={UserId}, Reason={Message}",
                                e.Id, userId.Value, loyaltyResponse.Message);
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "ℹ️ No phone number for loyalty: TxId={TxId}, User={UserId}",
                            e.Id, userId.Value);
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

            return new BaseResponse<TransactionDto>(true, null, "Game session created successfully.", transactionDto);


        }

        public async Task<BaseResponse<TransactionDto>> CreateCoffeeShopOrder(int? userId, int discountId, List<OrderItemRequest> itemsRequest,
            string createdBy, CancellationToken ct, string comment = "", bool isOpenInvoice = false, int? setId = null)
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

            
                var dbItems = await _repoItem.Query(false)
                    .Where(i => ids.Contains(i.Id))
                    .ToListAsync(ct); 

            
                if (dbItems.Count != ids.Count)
                {
                    var missing = ids.Except(dbItems.Select(i => i.Id)).ToList();
                    return new BaseResponse<TransactionDto>(false, "Invalid items", 
                        $"The following item IDs do not exist: {string.Join(", ", missing)}");
            }

            
                var outOfStock = new List<string>();
                foreach (var it in dbItems)
                {
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



            try
            {
                _logger.LogInformation("CS/Order BEFORE_SAVE ReqId={ReqId} Total={Total} Items={Count}",reqId, totalPrice, trxItems.Count);


                await _repo.AddAsync(trx, ct);
                await _repoTrxItem.AddRangeAsync(trxItems, ct);


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
            if (dto.RoomId.HasValue) e.RoomId = dto.RoomId;
            if (dto.SetId.HasValue) e.SetId = dto.SetId;
            if (dto.GameTypeId.HasValue) e.GameTypeId = dto.GameTypeId;
            if (dto.GameId.HasValue) e.GameId = dto.GameId;
            if (dto.GameSettingId.HasValue) e.GameSettingId = dto.GameSettingId;
            if (dto.Hours.HasValue) e.Hours = dto.Hours ?? 0;
            if (dto.TotalPrice.HasValue) e.TotalPrice = dto.TotalPrice ?? e.TotalPrice;
            if (dto.StatusId.HasValue) e.StatusId = dto.StatusId.Value;
            Discount? discount = null;

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
            await _uow.SaveChangesAsync(ct);
            return true;
        }
        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query(asNoTracking: false)
                .Include(t => t.TransactionItems)
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (e is null) return false;

            // If it's a coffee-shop order (no GameId), return stock
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
            }

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

            // 5) Rounding logic for hours (ONLY hour rounding stays)
            bool isBoardGame = tx.GameTypeId == 2;

            decimal GetBilledHours(decimal h)
            {
                if (h <= 0m || h <= 0.25m)
                    return 0m;

                // Minimum 1 hour for anything up to 1h 15m
                if (h <= 1.25m) // 1.25h = 1h 15min
                    return 1m;

                // From here, apply the repeating pattern around each whole hour
                var n = Math.Floor(h); // base whole hour: 1, 2, 3, ...

                // x:00 → x:15  => x.0
                if (h < n + 0.25m)
                    return n;

                // x:15 → x:45  => x.5
                if (h < n + 0.75m)
                    return n + 0.5m;

                // x:45 → (x+1):15  => x+1
                return n + 1m;
            }

            decimal billedHours = GetBilledHours(rawHours);

            // 6) Board game day-pass override
            decimal totalPriceBeforeDiscount;
            if (isBoardGame && rawHours > 2m)
            {
                var dayPass = await _repoSetting.Query()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.IsDayPass == true && s.GameId == tx.GameId, ct);

                if (dayPass != null && dayPass.Price > 0)
                {
                    totalPriceBeforeDiscount = dayPass.Price * persons;
                }
                else
                {
                    totalPriceBeforeDiscount = setting.Price * billedHours * persons;
                }
            }
            else
            {
                // Normal (PS5 + board games <=2h)
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

                    if (!string.IsNullOrWhiteSpace(userPhone) && await IsClientUserAsync(tracked.UserId.Value, ct) )
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


            var dto = _mapper.ToDto(tracked);
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
                .Include(t => t.GameSetting).AsSplitQuery()
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

            // Stock check
            var outOfStock = new List<string>();
            foreach (var it in dbItems)
            {
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

        public async Task<BaseResponse<TransactionDto>> CloseOpenInvoice( int invoiceId, string updatedBy,  CancellationToken ct)
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

            try
            {
                _logger.LogInformation(
                    "FNB/UpdateSet BEFORE_SAVE ReqId={ReqId} InvoiceId={InvoiceId} SetId={SetId}",
                    reqId, invoiceId, setId);

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

    }
}
