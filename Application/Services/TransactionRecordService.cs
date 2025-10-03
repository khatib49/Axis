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
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;

        public TransactionRecordService(IBaseRepository<TransactionRecord> repo, IBaseRepository<Setting> repoSetting,
            IBaseRepository<Room> repoRoom, IBaseRepository<Game> repoGame, IBaseRepository<Item> repoItem,
            IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
            _repoSetting = repoSetting;
            _repoRoom = repoRoom;
            _repoGame = repoGame;
            _repoItem = repoItem;
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
            var list = await _repo.Query()
                        .Include(s => s.Game)
                        .Include(s=> s.GameType)
                        .Include(s=> s.GameSetting)
                        .Include(s => s.Room)
                        .AsNoTracking()
                        .ToListAsync(ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

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
        
        public async Task<TransactionDto> CreateCoffeeShopOrder(string itemIds, CancellationToken ct)
        {

            #region Check if the Item quantity Avaliable
            bool available  = _repoItem.GetByIdAsync(Guid.Parse(itemIds), asNoTracking: true, ct) != null;
            #endregion

            #region Calculate the Total Price

            #endregion



            var e = _mapper.ToEntity(entity);
            e.CreatedBy = createdBy ?? "";
            e.CreatedOn = DateTime.UtcNow;
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
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
