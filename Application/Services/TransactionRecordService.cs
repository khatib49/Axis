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

        public TransactionRecordService(IBaseRepository<TransactionRecord> repo, IBaseRepository<Setting> repoSetting,
            IBaseRepository<Room> repoRoom, IBaseRepository<Game> repoGame, IBaseRepository<Item> repoItem,
            IBaseRepository<TransactionItem> repoTrxItem, IBaseRepository<Status> repoStatus, UserManager<AppUser> userManager, IBaseRepository<Discount> repoDiscount, IBaseRepository<Set> repoSet,
        IUnitOfWork uow, DomainMapper mapper, ILogger<TransactionRecordService> logger, IHttpContextAccessor httpContextAccessor)
        {
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
                e.Discount?.Name ?? string.Empty,
                e.numberOfPersons,
                e.GameSetting?.IsDayPass ?? false
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
                string createdBy, int roomSetId, int discountId, CancellationToken ct = default, int numberOfPersons = 1, bool isDayPass = false)
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
                    numberOfPersons: numberOfPersons
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

            #region No need for hangfire now
            //// schedule a job only if there is a time limit
            //if (!setting.IsOpenHour && expectedEndOn.HasValue)
            //{
            //    // Hangfire will call SessionEndMonitor.EndIfOngoingAsync at expected end
            //    var jobId = BackgroundJob.Schedule<SessionEndMonitor>(
            //                    x => x.EndIfOngoingAsync(e.Id, 6, CancellationToken.None),
            //                    expectedEndOn.Value - DateTime.UtcNow);

            //    // store job id
            //    var tracked = await _repo.GetByIdAsync(e.Id, asNoTracking: false, ct);
            //    if (tracked != null)
            //    {
            //        //tracked.HangfireJobId = jobId;
            //        await _uow.SaveChangesAsync(ct);
            //    }
            //}
            #endregion
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

        public async Task<BaseResponse<TransactionDto>> CreateCoffeeShopOrder(int? userId, int discountId, List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct)
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


            // Create transaction
            var trx = new TransactionRecord
                {
                
                    RoomId = null,
                    SetId = null,
                    GameTypeId = null,
                    GameId = null,
                    GameSettingId = null,
                    Hours = 0,
                    TotalPrice = totalPrice,
                    StatusId = 6,
                    UserId = userId,
                    CreatedBy = createdBy ?? "",
                    CreatedOn = DateTime.UtcNow,
                    DiscountId = discount?.Id,
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

                TransactionDto transactionDto =  _mapper.ToDto(trx);
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

            // must be processed & unpaid (7)
            if (tx.StatusId != 7)
                return new BaseResponse<TransactionDto>(false, "Invalid status", "This session is not in 'processed & unpaid' status.");

            // 2) Load game setting (price + IsOpenHour)
            var setting = await _repoSetting.Query()
                .AsNoTracking()
                .Where(s => s.Id == tx.GameSettingId)
                .Select(s => new { s.Hours, s.Price, s.IsOpenHour })
                .FirstOrDefaultAsync(ct);

            if (setting is null)
                return new BaseResponse<TransactionDto>(false, "Invalid game setting", "The game setting linked to this session does not exist.");

            // 3) Calculate played time
            var nowUtc = DateTime.UtcNow;
            var startedOn = tx.CreatedOn; // assuming non-null
            if (startedOn == default)
                return new BaseResponse<TransactionDto>(false, "Invalid data", "Session start time is missing.");

            var totalMinutes = (nowUtc - startedOn).TotalMinutes;
            if (totalMinutes < 1)
                totalMinutes = 1; // at least 1 minute

            // ---- ROUNDING LOGIC (30-min steps) ----
            // 1:00 - 1:30  -> 1:30
            // 1:31 - 1:59  -> 2:00
            var fullHours = Math.Floor(totalMinutes / 60.0);
            var remainingMinutes = totalMinutes - (fullHours * 60.0);

            if (remainingMinutes > 0 && remainingMinutes <= 30)
            {
                remainingMinutes = 30;
            }
            else if (remainingMinutes > 30)
            {
                fullHours += 1;
                remainingMinutes = 0;
            }

            var billedMinutes = (fullHours * 60.0) + remainingMinutes;   // e.g. 1h30 = 90
            decimal playedHours = (decimal)billedMinutes / 60m;          // e.g. 90/60 = 1.5
            playedHours = Math.Round(playedHours, 2, MidpointRounding.AwayFromZero);

            // 4) Persons
            var persons = tx.numberOfPersons > 0 ? tx.numberOfPersons : 1;

            // 5) Calculate total price (NEW LOGIC)
            // Price is per hour per person
            bool isBoardGame = tx.GameTypeId == 2; // 2 = board games // if we ar darts games
            decimal totalPriceBeforeDiscount;
            bool calculateThePrice = true;
            if (isBoardGame && playedHours > 2m)
            {
                // BOARD GAMES: if more than 2H -> use day-pass price
                // adapt the filter to your schema (e.g. GameTypeId, GameId, etc.)
                var dayPass = await _repoSetting.Query()
                    .AsNoTracking()
                    .Where(s => s.IsDayPass == true && s.GameId == tx.GameId)
                    .FirstOrDefaultAsync(ct);
                decimal dayPassPrice = dayPass.Price;
                if (dayPassPrice > 0)
                {
                    // day-pass price * persons
                    calculateThePrice = false;
                    totalPriceBeforeDiscount = dayPassPrice * persons;
                }
                else
                {
                    // fallback: charge normal hours if no day-pass configured
                    totalPriceBeforeDiscount = setting.Price * playedHours * persons;
                }
            }
            else
            {
                // Normal hourly pricing
                totalPriceBeforeDiscount = setting.Price * playedHours * persons;
            }

            decimal totalPrice = totalPriceBeforeDiscount;

            // 6) Apply discount if exists (UNCHANGED)
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


            // FINAL PRICE ROUNDING
            if (totalPrice > 0 && calculateThePrice)
            {
                var floor = Math.Floor(totalPrice);          // x
                var fraction = totalPrice - (decimal)floor;  // decimal part

                if (fraction == 0)
                {
                    // x.0 → stay x.0
                    totalPrice = (decimal)floor;
                }
                else if (fraction > 0 && fraction <= 0.5m)
                {
                    // x.1 - x.5 → x.5
                    totalPrice = (decimal)floor + 0.5m;
                }
                else
                {
                    // x.6 - x.9 → x+1
                    totalPrice = (decimal)floor + 1m;
                }
            }


            // if totalPrice == 0 (e.g. 100% discount), keep it 0

            // 7) Update tracked entity
            var tracked = await _repo.GetByIdAsync(invoiceId, asNoTracking: false, ct);
            if (tracked is null)
                return new BaseResponse<TransactionDto>(false, "Invalid invoice", "The specified invoice/transaction does not exist.");

            tracked.Hours = (int)Math.Ceiling(playedHours);    // store rounded hours
            tracked.TotalPrice = totalPrice;
            tracked.StatusId = 6; // processed & paid
            tracked.ModifiedOn = nowUtc;
            tracked.CreatedBy = updatedBy ?? tracked.CreatedBy;

            // 8) Make Set Available
            if (tx.SetId.HasValue)
            {
                var set = await _repoSet.Query(asNoTracking: false)
                    .FirstOrDefaultAsync(s => s.Id == tx.SetId.Value, ct);

                if (set != null)
                    set.StatusId = 9; // available
            }

            try
            {
                _logger.LogInformation(
                    "GS/CloseSession BEFORE_SAVE ReqId={ReqId} Tx={TxId} Hours={Hours} PlayedRounded={PlayedHours} Total={Total}",
                    reqId, tracked.Id, tracked.Hours, playedHours, tracked.TotalPrice);

                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var (prov, code) = ExtractDbCode(ex);
                _logger.LogError(ex,
                    "GS/CloseSession ERROR ReqId={ReqId} DB={Prov}:{Code} Tx={TxId} Sig={Sig}",
                    reqId, prov, code, invoiceId, sig);

                return new BaseResponse<TransactionDto>(false, "db error", "Failed to close the session. Please try again.");
            }

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


    }
}
