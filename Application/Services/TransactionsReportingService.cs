using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public partial class TransactionRecordService : ITransactionRecordService
    {
        public async Task<PaginatedResponse<ItemTransactionDto>> GetItemTransactionsWithDetailsAsync(
    TransactionsFilterDto f, CancellationToken ct = default)
        {
            var q = _repo.Query(); // IQueryable<TransactionRecord> (AsNoTracking in repo)

            // -------- Filters (dates, status, creator) --------
            if (f.From.HasValue) q = q.Where(t => t.CreatedOn >= f.From.Value);
            if (f.To.HasValue) q = q.Where(t => t.CreatedOn < f.To.Value);

            if (f.StatusIds is { Count: > 0 })
                q = q.Where(t => f.StatusIds!.Contains(t.StatusId));

            if (f.CreatedBy is { Count: > 0 })
                q = q.Where(t => f.CreatedBy!.Contains(t.CreatedBy));


                q = q.Where(t => t.Game == null);

            // -------- Item/Category filters & search --------
            if (f.CategoryIds is { Count: > 0 })
                q = q.Where(t => t.TransactionItems.Any(ti =>
                            ti.Item != null && f.CategoryIds!.Contains(ti.Item.CategoryId)));

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                q = q.Where(t => t.TransactionItems.Any(ti =>
                    ti.Item != null && (
                        ti.Item.Name.ToLower().Contains(s) ||
                        (ti.Item.Category != null && ti.Item.Category.Name.ToLower().Contains(s))
                    )));
            }

            // -------- Count before pagination --------
            var total = await q.CountAsync(ct);

            var page = Math.Max(1, f.Page);
            var size = Math.Max(1, f.PageSize);

            // -------- Project to DTO with nested items --------
            var data = await q
                .OrderByDescending(t => t.CreatedOn)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(t => new ItemTransactionDto
                {
                    TransactionId = t.Id,
                    CreatedOn = t.CreatedOn,
                    StatusId = t.StatusId,
                    CreatedBy = t.CreatedBy,

                    RoomId = t.RoomId,
                    RoomName = t.Room != null ? t.Room.Name : null,

                    // If you store set on transactions (SetId/Set)
                    SetId = t.SetId,
                    SetName = t.Set != null ? t.Set.Name : null,

                    Hours = t.Hours,
                    TotalPrice = t.TotalPrice,

                    Items = t.TransactionItems.Select(ti => new TransactionItemMiniDto
                    {
                        ItemId = ti.ItemId,
                        ItemName = ti.Item != null ? ti.Item.Name : string.Empty,

                        CategoryId = ti.Item != null ? ti.Item.CategoryId : 0,
                        CategoryName = (ti.Item != null && ti.Item.Category != null)
                                        ? t.TransactionItems.Where(x => x.ItemId == ti.ItemId)
                                          .Select(x => x.Item!.Category!.Name).FirstOrDefault()
                                        : null,

                        ItemType = ti.Item != null ? ti.Item.Type : string.Empty,
                        Quantity = ti.Quantity,
                        UnitPrice = ti.Item != null ? ti.Item.Price : 0m,
                        LineTotal = (ti.Item != null ? ti.Item.Price : 0m) * ti.Quantity,
                        ImagePath = ti.Item != null ? ti.Item.ImagePath : null
                    }).ToList()
                })
                .ToListAsync(ct);

            return new PaginatedResponse<ItemTransactionDto>(total, data, page, size);
        }

        public async Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(
    TransactionsFilterDto f, CancellationToken ct = default)
        {
            var q = _repo.Query(); // IQueryable<TransactionRecord>

            // -------- Filters (dates, status, creator) --------
            if (f.From.HasValue) q = q.Where(t => t.CreatedOn >= f.From.Value);
            if (f.To.HasValue) q = q.Where(t => t.CreatedOn < f.To.Value);

            if (f.StatusIds is { Count: > 0 })
                q = q.Where(t => f.StatusIds!.Contains(t.StatusId));

            if (f.CreatedBy is { Count: > 0 })
                q = q.Where(t => f.CreatedBy!.Contains(t.CreatedBy));

            // ensure only game transactions
            q = q.Where(t => t.GameId != null);

            // -------- Game category filter --------
            if (f.CategoryIds is { Count: > 0 })
                q = q.Where(t => t.Game != null && f.CategoryIds!.Contains(t.Game.CategoryId));

            // -------- Search on game/room/type/setting names --------
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                q = q.Where(t =>
                    (t.Game != null && t.Game.Name.ToLower().Contains(s)) ||
                    (t.Room != null && t.Room.Name!.ToLower().Contains(s)) ||
                    (t.GameType != null && t.GameType.Name.ToLower().Contains(s)) ||
                    (t.GameSetting != null && t.GameSetting.Name.ToLower().Contains(s)));
            }

            // -------- Count before pagination --------
            var total = await q.CountAsync(ct);

            var page = Math.Max(1, f.Page);
            var size = Math.Max(1, f.PageSize);

            // -------- Project to DTO (NO items) --------
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

                    // If you store set on transactions (SetId/Set)
                    SetId = t.SetId,
                    SetName = t.Set != null ? t.Set.Name : null,

                    GameTypeId = t.GameTypeId,
                    GameTypeName = t.GameType != null ? t.GameType.Name : null,

                    GameId = t.GameId,
                    GameName = t.Game != null ? t.Game.Name : string.Empty,

                    GameCategoryId = t.Game != null ? (int?)t.Game.CategoryId : null,
                    GameCategoryName = (t.Game != null && t.Game.Category != null)
                                        ? t.Game.Category.Name
                                        : null,

                    GameSettingId = t.GameSettingId,
                    GameSettingName = t.GameSetting != null ? t.GameSetting.Name : null,

                    Hours = t.Hours,
                    TotalPrice = t.TotalPrice,

                    // Explicitly omit items (if your serializer ignores nulls):
                    // Items = null
                })
                .ToListAsync(ct);

            return new PaginatedResponse<GameTransactionDetailsDto>(total, data, page, size);
        }


        public async Task<List<DailySalesDto>> GetDailySalesAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default)
        {
            // base query
            var q = _repo.Query(); // IQueryable<TransactionRecord>, AsNoTracking in repo

            // inclusive date range [from, to]
            var toExclusive = to?.Date.AddDays(1);
            if (from.HasValue) q = q.Where(t => t.CreatedOn >= from.Value);
            if (toExclusive.HasValue) q = q.Where(t => t.CreatedOn < toExclusive.Value);

            // parse "1,2,3" -> List<int>
            List<int> catList = new();
            if (!string.IsNullOrWhiteSpace(categoryIds))
            {
                catList = categoryIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                    .Where(n => n.HasValue)
                    .Select(n => n!.Value)
                    .ToList();
            }

            if (catList.Count > 0)
            {
                q = q.Where(t =>
                    (t.GameId != null && t.Game != null && catList.Contains(t.Game.CategoryId)) ||
                    (t.GameId == null && t.TransactionItems.Any(ti => ti.Item != null && catList.Contains(ti.Item.CategoryId)))
                );
            }

            // games totals per day (use TransactionRecord.TotalPrice)
            var gamesDaily = await q
                .Where(t => t.GameId != null)
                .GroupBy(t => t.CreatedOn.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalPrice) })
                .ToListAsync(ct);

            // items totals per day (sum Item.Price * Quantity)
            var itemsDaily = await q
                .Where(t => t.GameId == null)
                .SelectMany(t => t.TransactionItems.Select(ti => new
                {
                    Date = t.CreatedOn.Date,
                    LineTotal = (ti.Item != null ? ti.Item.Price : 0m) * ti.Quantity
                }))
                .GroupBy(x => x.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(x => x.LineTotal) })
                .ToListAsync(ct);

            // merge
            var gameDict = gamesDaily.ToDictionary(x => x.Date, x => x.Total);
            var itemDict = itemsDaily.ToDictionary(x => x.Date, x => x.Total);
            var allDates = gameDict.Keys.Union(itemDict.Keys).OrderBy(d => d).ToList();

            var result = allDates.Select(d =>
            {
                var items = itemDict.TryGetValue(d, out var it) ? it : 0m;
                var games = gameDict.TryGetValue(d, out var gt) ? gt : 0m;
                return new DailySalesDto(
                    Date: d,
                    ItemsTotal: items,
                    GamesTotal: games,
                    GrandTotal: items + games
                );
            }).ToList();

            // zero-fill missing days if range provided
            if (from.HasValue && toExclusive.HasValue)
            {
                var start = from.Value.Date;
                var endEx = toExclusive.Value.Date; // exclusive
                var days = Enumerable.Range(0, (endEx - start).Days).Select(i => start.AddDays(i));
                var dict = result.ToDictionary(x => x.Date);
                result = days.Select(d => dict.TryGetValue(d, out var v) ? v : new DailySalesDto(d, 0m, 0m, 0m)).ToList();
            }

            return result;
        }



    }
}