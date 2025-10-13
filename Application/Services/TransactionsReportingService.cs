using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public partial class TransactionRecordService : ITransactionRecordService
    {
        public async Task<PaginatedResponse<ItemTransactionLineDto>> GetItemTransactionsWithDetailsAsync(
           TransactionsFilterDto f, CancellationToken ct = default)
        {
            // Keep it as IQueryable<TransactionRecord>
            IQueryable<TransactionRecord> trxQuery = _repo.Query(); // AsNoTracking per your BaseRepository

            // Filters that don't require navigation loading
            if (f.From.HasValue) trxQuery = trxQuery.Where(t => t.CreatedOn >= f.From.Value);
            if (f.To.HasValue) trxQuery = trxQuery.Where(t => t.CreatedOn < f.To.Value);

            if (f.StatusIds is { Count: > 0 })
                trxQuery = trxQuery.Where(t => f.StatusIds!.Contains(t.StatusId));

            if (f.CreatedBy is { Count: > 0 })
                trxQuery = trxQuery.Where(t => f.CreatedBy!.Contains(t.CreatedBy));

            // Includes needed for Item/Category filters and search
            trxQuery = trxQuery
                .Include(t => t.TransactionItems)!.ThenInclude(ti => ti.Item)!.ThenInclude(i => i.Category);

            // Category filter on Item.CategoryId (int) – no HasValue/Value
            if (f.CategoryIds is { Count: > 0 })
                trxQuery = trxQuery.Where(t =>
                    t.TransactionItems!.Any(ti => ti.Item != null && f.CategoryIds!.Contains(ti.Item.CategoryId)));

            // Free-text search (Item/Category)
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                trxQuery = trxQuery.Where(t =>
                    t.TransactionItems!.Any(ti =>
                        ti.Item != null && (
                            ti.Item.Name.ToLower().Contains(s) ||
                            (ti.Item.Category != null && ti.Item.Category.Name.ToLower().Contains(s))
                        )));
            }

            // Flatten to one row per item line
            var flatQuery = trxQuery
                .SelectMany(t => t.TransactionItems!.Select(ti => new ItemTransactionLineDto
                {
                    TransactionId = t.Id,
                    CreatedOn = t.CreatedOn,
                    StatusId = t.StatusId,
                    CreatedBy = t.CreatedBy,

                    ItemId = ti.ItemId,
                    ItemName = ti.Item != null ? ti.Item.Name : string.Empty,
                    CategoryId = ti.Item != null ? ti.Item.CategoryId : 0,
                    CategoryName = (ti.Item != null && ti.Item.Category != null) ? ti.Item.Category.Name : null,
                    ItemType = ti.Item != null ? ti.Item.Type : string.Empty,
                    OrderedQuantity = ti.Quantity,
                    UnitPrice = ti.Item != null ? ti.Item.Price : 0m,
                    LineTotal = (ti.Item != null ? ti.Item.Price : 0m) * ti.Quantity,
                    ImagePath = ti.Item != null ? ti.Item.ImagePath : null
                }));

            var total = await flatQuery.CountAsync(ct);

            var page = Math.Max(1, f.Page);
            var size = Math.Max(1, f.PageSize);

            var data = await flatQuery
                .OrderByDescending(x => x.CreatedOn)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync(ct);

            return new PaginatedResponse<ItemTransactionLineDto>(total, data, page, size);
        }

        /// ONE ROW PER TransactionRecord (game transaction).
        public async Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(
            TransactionsFilterDto f, CancellationToken ct = default)
        {
            IQueryable<TransactionRecord> q = _repo.Query(); // AsNoTracking

            // Filters that don’t need navs
            if (f.From.HasValue) q = q.Where(t => t.CreatedOn >= f.From.Value);
            if (f.To.HasValue) q = q.Where(t => t.CreatedOn < f.To.Value);

            if (f.StatusIds is { Count: > 0 })
                q = q.Where(t => f.StatusIds!.Contains(t.StatusId));

            if (f.CreatedBy is { Count: > 0 })
                q = q.Where(t => f.CreatedBy!.Contains(t.CreatedBy));

            // Load game/type/setting/room
            q = q.Include(t => t.Game)!.ThenInclude(g => g.Category)
                 .Include(t => t.GameType)
                 .Include(t => t.GameSetting)
                 .Include(t => t.Room);

            // Category filter on Game.CategoryId (int) – no HasValue/Value
            if (f.CategoryIds is { Count: > 0 })
                q = q.Where(t => t.Game != null && f.CategoryIds!.Contains(t.Game.CategoryId));

            // Search
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                q = q.Where(t =>
                    (t.Game != null && t.Game.Name.ToLower().Contains(s)) ||
                    (t.Room != null && t.Room.Name!.ToLower().Contains(s)) ||
                    (t.GameType != null && t.GameType.Name.ToLower().Contains(s)) ||
                    (t.GameSetting != null && t.GameSetting.Name.ToLower().Contains(s)));
            }

            var total = await q.CountAsync(ct);

            var page = Math.Max(1, f.Page);
            var size = Math.Max(1, f.PageSize);

            var data = await q
                .OrderByDescending(t => t.CreatedOn)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(t => new GameTransactionDetailsDto
                {
                    TransactionId = t.Id,
                    CreatedOn = t.CreatedOn,
                    StatusId = t.StatusId,
                    CreatedBy = t.CreatedBy,

                    RoomId = t.RoomId,
                    RoomName = t.Room != null ? t.Room.Name : null,

                    GameTypeId = t.GameTypeId,
                    GameTypeName = t.GameType != null ? t.GameType.Name : null,

                    GameId = t.GameId,
                    GameName = t.Game != null ? t.Game.Name : string.Empty,

                    // Project int -> int? safely
                    GameCategoryId = t.Game != null ? (int?)t.Game.CategoryId : null,
                    GameCategoryName = (t.Game != null && t.Game.Category != null) ? t.Game.Category.Name : null,

                    GameSettingId = t.GameSettingId,
                    GameSettingName = t.GameSetting != null ? t.GameSetting.Name : null,

                    Hours = t.Hours,
                    TotalPrice = t.TotalPrice
                })
                .ToListAsync(ct);

            return new PaginatedResponse<GameTransactionDetailsDto>(total, data, page, size);
        }
    }
}