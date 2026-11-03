using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Floor
{
    public class FloorService : IFloorService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<FloorService> _logger;

        public FloorService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<FloorService> logger)
        {
            _db = db;
            _user = user;
            _logger = logger;
        }

        // ── Areas ────────────────────────────────────────────────────
        public async Task<List<FloorAreaDto>> ListAreasAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<FloorArea>().AsNoTracking();
            if (!includeInactive) q = q.Where(a => a.IsActive);
            return await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                .Select(a => new FloorAreaDto(a.Id, a.Name, a.SortOrder, a.IsActive, a.BranchId, a.Tables.Count))
                .ToListAsync(ct);
        }

        public async Task<FloorAreaDto> CreateAreaAsync(FloorAreaUpsertDto dto, CancellationToken ct = default)
        {
            var e = new FloorArea
            {
                Name = (dto.Name ?? string.Empty).Trim(),
                SortOrder = dto.SortOrder,
                IsActive = dto.IsActive,
                BranchId = _user.BranchId,
            };
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            return new FloorAreaDto(e.Id, e.Name, e.SortOrder, e.IsActive, e.BranchId, 0);
        }

        public async Task<BaseResponse<FloorAreaDto>> UpdateAreaAsync(int id, FloorAreaUpsertDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<FloorArea>().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (e is null) return new BaseResponse<FloorAreaDto>(false, "Not found", "Area not found.");
            if (string.IsNullOrWhiteSpace(dto.Name)) return new BaseResponse<FloorAreaDto>(false, "Validation", "Name is required.");
            e.Name = dto.Name.Trim();
            e.SortOrder = dto.SortOrder;
            e.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            var count = await _db.Set<DiningTable>().CountAsync(t => t.AreaId == id, ct);
            return new BaseResponse<FloorAreaDto>(true, null, "Updated", new FloorAreaDto(e.Id, e.Name, e.SortOrder, e.IsActive, e.BranchId, count));
        }

        public async Task<BaseResponse<bool>> DeleteAreaAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<FloorArea>().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (e is null) return new BaseResponse<bool>(false, "Not found", "Area not found.");
            var hasTables = await _db.Set<DiningTable>().AnyAsync(t => t.AreaId == id, ct);
            if (hasTables)
            {
                // Soft-delete: tables (and their history) stay intact.
                e.IsActive = false;
                await _db.SaveChangesAsync(ct);
                return new BaseResponse<bool>(true, null, "Area has tables; deactivated instead of deleted.", true);
            }
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<bool>(true, null, "Deleted", true);
        }

        // ── Tables ───────────────────────────────────────────────────
        public async Task<List<DiningTableDto>> ListTablesAsync(int? areaId, bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<DiningTable>().AsNoTracking();
            if (areaId.HasValue) q = q.Where(t => t.AreaId == areaId.Value);
            if (!includeInactive) q = q.Where(t => t.IsActive);
            return await q.OrderBy(t => t.Area.SortOrder).ThenBy(t => t.Code)
                .Select(t => new DiningTableDto(t.Id, t.AreaId, t.Area.Name, t.Code, t.Name, t.Capacity, t.Shape,
                    t.PosX, t.PosY, t.Width, t.Height, t.IsActive, t.BranchId))
                .ToListAsync(ct);
        }

        private static string? ValidateTable(DiningTableUpsertDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code)) return "Code is required.";
            if (dto.Code.Trim().Length > 20) return "Code is too long (max 20).";
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
            if (dto.Capacity <= 0) return "Capacity must be at least 1.";
            if (dto.Shape != "Rect" && dto.Shape != "Round") return "Shape must be Rect or Round.";
            if (dto.Width <= 0 || dto.Height <= 0) return "Width and height must be positive.";
            if (dto.PosX < 0 || dto.PosY < 0) return "Position cannot be negative.";
            return null;
        }

        public async Task<BaseResponse<DiningTableDto>> CreateTableAsync(DiningTableUpsertDto dto, CancellationToken ct = default)
        {
            var err = ValidateTable(dto);
            if (err != null) return new BaseResponse<DiningTableDto>(false, "Validation", err);
            var area = await _db.Set<FloorArea>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.AreaId, ct);
            if (area is null) return new BaseResponse<DiningTableDto>(false, "Validation", "Area not found.");
            var code = dto.Code.Trim();
            if (await _db.Set<DiningTable>().AnyAsync(t => t.Code == code, ct))
                return new BaseResponse<DiningTableDto>(false, "Validation", $"Table code '{code}' already exists.");

            var e = new DiningTable
            {
                AreaId = dto.AreaId,
                Code = code,
                Name = dto.Name.Trim(),
                Capacity = dto.Capacity,
                Shape = dto.Shape,
                PosX = dto.PosX,
                PosY = dto.PosY,
                Width = dto.Width,
                Height = dto.Height,
                IsActive = dto.IsActive,
                BranchId = _user.BranchId,
            };
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<DiningTableDto>(true, null, "Created", ToDto(e, area.Name));
        }

        public async Task<BaseResponse<DiningTableDto>> UpdateTableAsync(int id, DiningTableUpsertDto dto, CancellationToken ct = default)
        {
            var err = ValidateTable(dto);
            if (err != null) return new BaseResponse<DiningTableDto>(false, "Validation", err);
            var e = await _db.Set<DiningTable>().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (e is null) return new BaseResponse<DiningTableDto>(false, "Not found", "Table not found.");
            var area = await _db.Set<FloorArea>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.AreaId, ct);
            if (area is null) return new BaseResponse<DiningTableDto>(false, "Validation", "Area not found.");
            var code = dto.Code.Trim();
            if (await _db.Set<DiningTable>().AnyAsync(t => t.Code == code && t.Id != id, ct))
                return new BaseResponse<DiningTableDto>(false, "Validation", $"Table code '{code}' already exists.");

            e.AreaId = dto.AreaId;
            e.Code = code;
            e.Name = dto.Name.Trim();
            e.Capacity = dto.Capacity;
            e.Shape = dto.Shape;
            e.PosX = dto.PosX;
            e.PosY = dto.PosY;
            e.Width = dto.Width;
            e.Height = dto.Height;
            e.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<DiningTableDto>(true, null, "Updated", ToDto(e, area.Name));
        }

        public async Task<BaseResponse<bool>> DeleteTableAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<DiningTable>().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (e is null) return new BaseResponse<bool>(false, "Not found", "Table not found.");
            if (await _db.Set<TableSession>().AnyAsync(s => s.TableId == id && s.Status == TableSession.StatusOpen, ct))
                return new BaseResponse<bool>(false, "Validation", "Table has an open session.");
            var hasHistory = await _db.Set<TableSession>().AnyAsync(s => s.TableId == id, ct)
                             || await _db.Set<Reservation>().AnyAsync(r => r.TableId == id, ct);
            if (hasHistory)
            {
                e.IsActive = false;
                await _db.SaveChangesAsync(ct);
                return new BaseResponse<bool>(true, null, "Table has history; deactivated instead of deleted.", true);
            }
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<bool>(true, null, "Deleted", true);
        }

        public async Task<BaseResponse<int>> SaveLayoutAsync(BulkLayoutSaveDto dto, CancellationToken ct = default)
        {
            if (dto.Positions is null || dto.Positions.Count == 0)
                return new BaseResponse<int>(false, "Validation", "No positions provided.");
            var ids = dto.Positions.Select(p => p.Id).Distinct().ToList();
            var tables = await _db.Set<DiningTable>().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            var n = 0;
            foreach (var p in dto.Positions)
            {
                var t = tables.FirstOrDefault(x => x.Id == p.Id);
                if (t is null) continue;
                if (p.Width <= 0 || p.Height <= 0 || p.PosX < 0 || p.PosY < 0)
                    return new BaseResponse<int>(false, "Validation", $"Invalid geometry for table #{p.Id}.");
                t.PosX = p.PosX; t.PosY = p.PosY; t.Width = p.Width; t.Height = p.Height;
                n++;
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Floor layout saved by {User}: {Count} tables", _user.UserName, n);
            return new BaseResponse<int>(true, null, "Layout saved", n);
        }

        // ── Live layout ──────────────────────────────────────────────
        public async Task<FloorLayoutDto> GetLayoutAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var areas = await _db.Set<FloorArea>().AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
                .ToListAsync(ct);
            var tables = await _db.Set<DiningTable>().AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Code)
                .ToListAsync(ct);

            var open = await (
                from s in _db.Set<TableSession>().AsNoTracking()
                where s.Status == TableSession.StatusOpen
                join t in _db.Set<TransactionRecord>().AsNoTracking() on s.TransactionId equals t.Id
                select new
                {
                    s.Id, s.TableId, s.TransactionId, s.GuestCount, s.OpenedOn, s.OpenedBy, s.Notes,
                    t.TotalPrice,
                    ItemsCount = t.TransactionItems.Sum(i => (int?)i.Quantity) ?? 0,
                }).ToListAsync(ct);

            var openByTable = open.GroupBy(o => o.TableId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OpenedOn).First());

            var result = areas.Select(a => new LayoutAreaDto(a.Id, a.Name, a.SortOrder,
                tables.Where(t => t.AreaId == a.Id).Select(t =>
                {
                    openByTable.TryGetValue(t.Id, out var o);
                    var summary = o is null ? null : new OpenSessionSummaryDto(o.Id, o.TransactionId, o.TotalPrice, o.GuestCount,
                        o.OpenedOn, (int)Math.Max(0, (now - o.OpenedOn).TotalMinutes), o.ItemsCount, o.OpenedBy, o.Notes);
                    return new LayoutTableDto(t.Id, t.AreaId, t.Code, t.Name, t.Capacity, t.Shape, t.PosX, t.PosY, t.Width, t.Height,
                        t.IsActive, summary);
                }).ToList())).ToList();

            return new FloorLayoutDto(result, now);
        }

        private static DiningTableDto ToDto(DiningTable t, string areaName) =>
            new(t.Id, t.AreaId, areaName, t.Code, t.Name, t.Capacity, t.Shape, t.PosX, t.PosY, t.Width, t.Height, t.IsActive, t.BranchId);
    }
}
