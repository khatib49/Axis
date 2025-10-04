using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class TransactionRecordService : ITransactionRecordService
    {
        private readonly IBaseRepository<TransactionRecord> _repo;
        private readonly IBaseRepository<Setting> _repoSetting;
        private readonly IBaseRepository<Room> _repoRoom;
        private readonly IBaseRepository<Game> _repoGame;
        private readonly IBaseRepository<Item> _repoItem;
        private readonly IBaseRepository<TransactionItem> _repoTrxItem;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;

        public TransactionRecordService(IBaseRepository<TransactionRecord> repo, IBaseRepository<Setting> repoSetting,
            IBaseRepository<Room> repoRoom, IBaseRepository<Game> repoGame, IBaseRepository<Item> repoItem,
            IBaseRepository<TransactionItem> repoTrxItem,
            IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
            _repoSetting = repoSetting;
            _repoRoom = repoRoom;
            _repoGame = repoGame;
            _repoItem = repoItem;
            _repoTrxItem = repoTrxItem;
        }

        public async Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _repo.Query()
                    .Include(s => s.Game)
                    .Include(s => s.GameType)
                    .Include(s => s.GameSetting)
                    .Include(s => s.Room)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == id, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<TransactionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            // Start with base query
            var query = _repo.QueryableAsync()
                        .Include(s => s.Game)
                        .Include(s => s.GameType)
                        .Include(s => s.GameSetting)
                        .Include(s => s.Room)
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


        public async Task<TransactionDto> CreateGameSession(Guid gameId, Guid gameSettingId, int hours, Guid statusid, string createdBy, CancellationToken ct = default)
        {

            #region Check if the Room Available
            var game = await _repoGame.Query()
                .AsNoTracking()
                .Where(s => s.Id == gameId)
                .FirstOrDefaultAsync(ct);
            if(game == null)
                throw new ArgumentException("Invalid game ID");

            var room = await _repoRoom.Query()
                .AsNoTracking()
                .Where(s => s.CategoryId == game.CategoryId)
                .FirstOrDefaultAsync(ct);

            if(room == null)
                throw new InvalidOperationException("No available room for the selected game type.");


            // check if there is trnx ongoing for the same room and compare it with number of set

            var ongoingTrnx = await _repo.Query()
                .AsNoTracking()
                .Where(s => s.RoomId == room.Id && s.StatusId == statusid) // status id should be equal to ongoing status
                .CountAsync(ct);

            if (ongoingTrnx >= room.Sets)
            {
                throw new InvalidOperationException("No available room for the selected game type.");
            }
            #endregion
            Setting? settingDto = await  _repoSetting.Query()
                .AsNoTracking()
                .Where(s => s.Id == gameSettingId)
                .Select(s => new Setting
                {
                    Id = s.Id,
                    Hours = s.Hours,
                    Price = s.Price,
                })
                .FirstOrDefaultAsync(ct);

            if (settingDto == null)
            {
                throw new ArgumentException("Invalid game setting ID");
            }

            if (settingDto.Hours <= 0)
                throw new InvalidOperationException("Configured Hours must be > 0 for price calculation.");


            decimal totalPrice = settingDto.Price * hours / settingDto.Hours;

            var entity = new TransactionCreateDto(
                                RoomId: room.Id,
                                GameTypeId: game.CategoryId,
                                GameId: gameId,
                                GameSettingId: gameSettingId,
                                Hours: hours,
                                TotalPrice: totalPrice,
                                StatusId: statusid,
                                CreatedOn: DateTime.UtcNow,
                                CreatedBy: createdBy ?? string.Empty
                            );


            var e = _mapper.ToEntity(entity);
            e.CreatedBy = createdBy ?? "";
            e.CreatedOn = DateTime.UtcNow;
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }


        public async Task<TransactionDto> CreateCoffeeShopOrder(List<OrderItemRequest> itemsRequest, string createdBy, CancellationToken ct)
        {
            if (itemsRequest is null || itemsRequest.Count == 0)
                throw new ArgumentException("No items provided.");

            
            var requested = itemsRequest
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            
            var invalidQty = requested.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList();
            if (invalidQty.Any())
                throw new ArgumentException($"Invalid quantity (<=0) for items: {string.Join(", ", invalidQty)}");

            var ids = requested.Keys.ToList();

            
            var dbItems = await _repoItem.Query()
                .Where(i => ids.Contains(i.Id))
                .ToListAsync(ct); 

            
            if (dbItems.Count != ids.Count)
            {
                var missing = ids.Except(dbItems.Select(i => i.Id)).ToList();
                throw new ArgumentException($"Items not found: {string.Join(", ", missing)}");
            }

            
            var outOfStock = new List<string>();
            foreach (var it in dbItems)
            {
                var need = requested[it.Id];
                if (it.Quantity < need)
                    outOfStock.Add($"{it.Name} (needs {need}, has {it.Quantity})"); 
            }
            if (outOfStock.Any())
                throw new InvalidOperationException($"Insufficient stock for: {string.Join(", ", outOfStock)}");

            // Compute total
            decimal totalPrice = 0m;
            foreach (var it in dbItems)
            {
                var qty = requested[it.Id];
                totalPrice += (it.Price * qty);
            }

            // Create transaction
            var trx = new TransactionRecord
            {
                Id = Guid.NewGuid(),
                RoomId = null,
                GameTypeId = null,
                GameId = null,
                GameSettingId = null,
                Hours = 0,
                TotalPrice = totalPrice,
                StatusId = Guid.Parse("f779b04d-9808-429a-9545-4fd36fa0b1e5"),
                CreatedBy = createdBy ?? "",
                CreatedOn = DateTime.UtcNow,
            };

            
            var trxItems = new List<TransactionItem>();
            foreach (var it in dbItems)
            {
                var qty = requested[it.Id];
                trxItems.Add(new TransactionItem
                {
                    
                    TransactionRecordId = trx.Id, 
                    ItemId = it.Id,
                    Quantity = qty,
                });

                
                it.Quantity -= qty;
            }

            
            await _repo.AddAsync(trx, ct);
            await _repoTrxItem.AddRangeAsync(trxItems, ct);

           
            await _uow.SaveChangesAsync(ct);

            return _mapper.ToDto(trx);
        }

        public async Task<bool> UpdateAsync(Guid id, TransactionUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;
            e.ModifiedOn = DateTime.UtcNow;
            _mapper.MapTo(dto, e); // updates only non-null fields
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
