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

            // -------- Project to DTO with nested items --------
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

                    Items = t.TransactionItems.Select(ti => new TransactionItemMiniDto
                    {
                        ItemId = ti.ItemId,
                        ItemName = ti.Item != null ? ti.Item.Name : string.Empty,

                        CategoryId = ti.Item != null ? ti.Item.CategoryId : 0,
                        CategoryName = (ti.Item != null && ti.Item.Category != null)
                                        ? ti.Item.Category.Name
                                        : null,

                        ItemType = ti.Item != null ? ti.Item.Type : string.Empty,
                        Quantity = ti.Quantity,
                        UnitPrice = ti.Item != null ? ti.Item.Price : 0m,
                        LineTotal = (ti.Item != null ? ti.Item.Price : 0m) * ti.Quantity,
                        ImagePath = ti.Item != null ? ti.Item.ImagePath : null
                    }).ToList()
                })
                .ToListAsync(ct);

            return new PaginatedResponse<GameTransactionDetailsDto>(total, data, page, size);
        }



    }
}