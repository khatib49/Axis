using Application.DTOs;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class SetService
    {
        private readonly IBaseRepository<Set> _repoSet;
        private readonly IBaseRepository<Room> _repoRoom;
        private readonly IBaseRepository<TransactionRecord> _repoTrx;
        private readonly IUnitOfWork _uow;

        public SetService(
            IBaseRepository<Set> repoSet,
            IBaseRepository<Room> repoRoom,
            IBaseRepository<TransactionRecord> repoTrx,
            IUnitOfWork uow)
        {
            _repoSet = repoSet;
            _repoRoom = repoRoom;
            _repoTrx = repoTrx;
            _uow = uow;
        }

        public async Task<RoomSetDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var q = _repoSet.Query()
                .Include(s => s.Room)
                .AsNoTracking();

            var e = await q.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (e is null) return null;

            return new RoomSetDto(e.Id, e.RoomId, e.Room?.Name ?? string.Empty, e.Name);
        }

        public async Task<PaginatedResponse<RoomSetDto>> ListAsync(RoomSetListFilterDto f, CancellationToken ct = default)
        {
            var q = _repoSet.Query()
                .Include(s => s.Room)
                .AsNoTracking();

            if (f.RoomId.HasValue)
                q = q.Where(s => s.RoomId == f.RoomId.Value);

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                q = q.Where(x =>
                    x.Name.ToLower().Contains(s) ||
                    (x.Room != null && x.Room.Name.ToLower().Contains(s)));
            }

            var total = await q.CountAsync(ct);

            var page = Math.Max(1, f.Page);
            var size = Math.Max(1, f.PageSize);

            var data = await q.OrderBy(x => x.RoomId).ThenBy(x => x.Name)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(e => new RoomSetDto(e.Id, e.RoomId, e.Room != null ? e.Room.Name : string.Empty, e.Name))
                .ToListAsync(ct);

            return new PaginatedResponse<RoomSetDto>(total, data, page, size);
        }

        public async Task<RoomSetDto> CreateAsync(RoomSetCreateDto dto, CancellationToken ct = default)
        {
            // 1) Validate room exists
            var roomExists = await _repoRoom.Query().AsNoTracking().AnyAsync(r => r.Id == dto.RoomId, ct);
            if (!roomExists) throw new ArgumentException($"Room {dto.RoomId} not found.");

            // 2) Enforce uniqueness (RoomId + Name)
            var name = dto.Name.Trim();
            var exists = await _repoSet.Query().AsNoTracking()
                .AnyAsync(s => s.RoomId == dto.RoomId && s.Name.ToLower() == name.ToLower(), ct);
            if (exists) throw new InvalidOperationException($"Set '{dto.Name}' already exists for room {dto.RoomId}.");

            var e = new Set
            {
                RoomId = dto.RoomId,
                Name = name
            };

            await _repoSet.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);

            // Load room name for DTO
            var roomName = await _repoRoom.Query().Where(r => r.Id == e.RoomId).Select(r => r.Name).FirstAsync(ct);
            return new RoomSetDto(e.Id, e.RoomId, roomName, e.Name);
        }

        public async Task<bool> UpdateAsync(int id, RoomSetUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repoSet.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            var newRoomId = dto.RoomId ?? e.RoomId;
            var newName = (dto.Name ?? e.Name).Trim();

            // If moving to another room or renaming within same room, check uniqueness
            var conflict = await _repoSet.Query()
                .AsNoTracking()
                .AnyAsync(s => s.Id != id
                               && s.RoomId == newRoomId
                               && s.Name.ToLower() == newName.ToLower(), ct);
            if (conflict)
                throw new InvalidOperationException($"Set '{newName}' already exists for room {newRoomId}.");

            // If room changed, validate new room exists
            if (dto.RoomId.HasValue && dto.RoomId.Value != e.RoomId)
            {
                var roomExists = await _repoRoom.Query()
                    .AsNoTracking()
                    .AnyAsync(r => r.Id == dto.RoomId.Value, ct);
                if (!roomExists) throw new ArgumentException($"Room {dto.RoomId.Value} not found.");
            }

            e.RoomId = newRoomId;
            e.Name = newName;

            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repoSet.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            // Prevent delete if any transaction references this set
            var inUse = await _repoTrx.Query().AsNoTracking()
                .AnyAsync(t => t.SetId == id, ct);
            if (inUse)
                throw new InvalidOperationException("Cannot delete: one or more transactions reference this set.");

            _repoSet.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

    }
}
