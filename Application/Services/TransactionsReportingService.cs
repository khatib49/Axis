using Application.DTOs;
using Application.IServices;
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
            if (f.From.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= f.From.Value);
            if (f.To.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < f.To.Value);

            if (f.StatusIds is { Count: > 0 })
                q = q.Where(t => f.StatusIds!.Contains(t.StatusId));

            if (f.CreatedBy is { Count: > 0 })
                q = q.Where(t => f.CreatedBy!.Contains(t.CreatedBy));

            q = q.Where(t => t.Game == null);
            q = q.Where(t => t.StatusId == 6 || t.StatusId == 7 || t.StatusId == 5);
                //q = q.Where(t => f.StatusIds!.Contains(t.StatusId));

            // -------- Item/Category filters & search --------
            if (f.CategoryIds is { Count: > 0 })
                q = q.Where(t => t.TransactionItems.Any(ti =>
                            ti.Item != null && f.CategoryIds!.Contains(ti.Item.CategoryId)));

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();

                q = q.Where(t =>
                    t.Id.ToString().ToLower().Contains(s) ||
                    t.TransactionItems.Any(ti =>
                        ti.Item != null && (
                            ti.Item.Name.ToLower().Contains(s) ||
                            (ti.Item.Category != null && ti.Item.Category.Name.ToLower().Contains(s))
                        )
                    )
                );
            }

            // -------- Count before pagination --------
            var total = await q.CountAsync(ct);
            var totalInvoices = await q.SumAsync(t => (decimal?)t.TotalPrice ?? 0, ct);

            var page = Math.Max(1, f.Page);
            var size = Math.Max(1, f.PageSize);

            // -------- Project to DTO with nested items --------
            var data = await q
                .OrderByDescending(t => t.CreatedOn)
                .Skip((page - 1) * size)
                .Take(size)
                .Include(t => t.Discount)
                .Select(t => new ItemTransactionDto
                {
                    TransactionId = t.Id,
                    CreatedOn = t.CreatedOn,
                    StatusId = t.StatusId,
                    CreatedBy = t.CreatedBy,
                    Comment = t.Comment,
                    RoomId = t.RoomId,
                    RoomName = t.Room != null ? t.Room.Name : null,
                    
                    // If you store set on transactions (SetId/Set)
                    SetId = t.SetId,
                    SetName = t.Set != null ? t.Set.Name : null,

                    Hours = t.Hours,
                    TotalPrice = t.TotalPrice,

                    // On the entity all along, just never projected.
                    NumberOfPersons = t.numberOfPersons,
                    UserId = t.UserId,
                    UserName = t.User != null
                        ? (t.User.DisplayName
                            ?? (((t.User.FirstName ?? "") + " " + (t.User.LastName ?? "")).Trim() != ""
                                ? ((t.User.FirstName ?? "") + " " + (t.User.LastName ?? "")).Trim()
                                : t.User.UserName))
                        : null,
                    ChannelId = t.ChannelId,
                    ChannelName = t.Channel != null ? t.Channel.Name : null,
                    ModifiedOn = t.ModifiedOn,

                    Discount = t.DiscountId != null && t.Discount != null
                    ? new DiscountDto(
                        t.Discount.Id,
                        t.Discount.Name,
                        t.Discount.Type,
                        t.Discount.Description,
                        t.Discount.Percentage,
                        t.Discount.IsActive,
                        t.Discount.CreatedOn,
                        t.Discount.UpdatedOn
                      )
                    : null,


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
                        // Included lines are worth zero — they came bundled
                        // with an event setting.
                        UnitPrice = ti.IsIncluded ? 0m : (ti.Item != null ? ti.Item.Price : 0m),
                        LineTotal = ti.IsIncluded ? 0m : (ti.Item != null ? ti.Item.Price : 0m) * ti.Quantity,
                        IsIncluded = ti.IsIncluded,
                        AddOns = ti.AddOns
                            .OrderBy(a => a.Id)
                            .Select(a => new OrderLineAddOnDto(
                                a.AddOnId, a.Name, a.Quantity, a.UnitPrice,
                                a.UnitPrice * a.Quantity))
                            .ToList(),
                        Variants = ti.Variants
                            .OrderBy(v => v.Id)
                            .Select(v => new OrderLineVariantDto(v.VariantId, v.Name, v.Quantity, v.PriceDelta))
                            .ToList(),
                        ImagePath = ti.Item != null ? ti.Item.ImagePath : null
                    }).ToList()
                }).Where(t => t.StatusId == 6 || t.StatusId == 7 || t.StatusId == 5)
                .ToListAsync(ct);

            return new PaginatedResponse<ItemTransactionDto>(total, data, page, size, totalInvoices);
        }

        public async Task<PaginatedResponse<GameTransactionDetailsDto>> GetGameTransactionsWithDetailsAsync(TransactionsFilterDto f,
            CancellationToken ct = default)
        {
            var q = _repo.Query(); // IQueryable<TransactionRecord>

            // -------- Filters (dates, status, creator) --------
            if (f.From.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= f.From.Value);
            if (f.To.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < f.To.Value);

            if (f.StatusIds is { Count: > 0 })
                q = q.Where(t => f.StatusIds!.Contains(t.StatusId));

            if (f.CreatedBy is { Count: > 0 })
                q = q.Where(t => f.CreatedBy!.Contains(t.CreatedBy));

            // ensure only game transactions
            q = q.Where(t => t.GameId != null);

            q = q.Where(t => t.StatusId == 6);
            // -------- Game category filter --------
            if (f.CategoryIds is { Count: > 0 })
                q = q.Where(t => t.Game != null && f.CategoryIds!.Contains(t.Game.CategoryId));

            q= q.Where(t => t.StatusId == 6);

            // -------- Search on game/room/type/setting names --------
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                q = q.Where(t =>
                    (t.Game != null && t.Game.Name.ToLower().Contains(s)) ||
                    (t.Room != null && t.Room.Name!.ToLower().Contains(s)) ||
                    (t.GameType != null && t.GameType.Name.ToLower().Contains(s)) ||
                    (t.Id.ToString().ToLower().Contains(s)) ||
                    (t.GameSetting != null && t.GameSetting.Name.ToLower().Contains(s)));
            }

            // -------- Count before pagination --------
            var total = await q.CountAsync(ct);
            var totalInvoices = await q.SumAsync(t => (decimal?)t.TotalPrice ?? 0, ct);

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
                    Comment = t.Comment,
                    GameSettingId = t.GameSettingId,
                    GameSettingName = t.GameSetting != null ? t.GameSetting.Name : null,

                    Hours = t.Hours,
                    TotalPrice = t.TotalPrice,

                    // Previously dropped on the floor — the admin panel had no
                    // way to see headcount, the attached client, or the channel.
                    NumberOfPersons = t.numberOfPersons,
                    UserId = t.UserId,
                    UserName = t.User != null
                        ? (t.User.DisplayName
                            ?? (((t.User.FirstName ?? "") + " " + (t.User.LastName ?? "")).Trim() != ""
                                ? ((t.User.FirstName ?? "") + " " + (t.User.LastName ?? "")).Trim()
                                : t.User.UserName))
                        : null,
                    ChannelId = t.ChannelId,
                    ChannelName = t.Channel != null ? t.Channel.Name : null,
                    ExpectedEndOn = t.ExpectedEndOn,
                    ModifiedOn = t.ModifiedOn,
                    IsDayPass = t.GameSetting != null && t.GameSetting.IsDayPass,

                    Discount = t.DiscountId != null && t.Discount != null
                    ? new DiscountDto(
                        t.Discount.Id,
                        t.Discount.Name,
                        t.Discount.Type,
                        t.Discount.Description,
                        t.Discount.Percentage,
                        t.Discount.IsActive,
                        t.Discount.CreatedOn,
                        t.Discount.UpdatedOn
                      )
                    : null

                    // Explicitly omit items (if your serializer ignores nulls):
                    // Items = null
                }).Where( c => c.StatusId==6)
                .ToListAsync(ct);

            return new PaginatedResponse<GameTransactionDetailsDto>(total, data, page, size, totalInvoices);
        }

        /// <summary>
        /// Paid event tickets in [from, toExclusive) by confirmation time. Only
        /// counted when no category filter is applied — tickets have no item /
        /// game category, so a filtered view is sales-only by definition.
        /// </summary>
        private IQueryable<Domain.Entities.EventRegistration> PaidEventsQuery(DateTime? from, DateTime? toExclusive)
        {
            static DateTime AsUtc(DateTime d) =>
                d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
                : d;
            var q = _repoEventReg.Query().Where(r => r.PaymentStatus == "Paid");
            if (from.HasValue) { var f = AsUtc(from.Value); q = q.Where(r => (r.ConfirmedOn ?? r.CreatedOn) >= f); }
            if (toExclusive.HasValue) { var t = AsUtc(toExclusive.Value); q = q.Where(r => (r.ConfirmedOn ?? r.CreatedOn) < t); }
            return q;
        }

        public async Task<PeriodTotalsDto> GetTotalsAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default, string? segment = null)
        {
            var q = _repo.Query();

            var toExclusive = to?.Date.AddDays(1);
            if (from.HasValue)
                q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= from.Value);
            if (toExclusive.HasValue)
                q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < toExclusive.Value);

            q = q.Where(t => t.StatusId == 6);

            // ── Segment totals (stable, discount-aware) ─────────────────
            // "fnb" / "tcg" / "gaming" / "items": the same allocation rule as
            // the accounting dashboard — each invoice's paid TotalPrice is
            // split across its lines by list-price weight, and a line belongs
            // to TCG when its category ItemType is Retail. A category-id list
            // could never do this: a basket with coffee + sleeves counted its
            // whole total in both buckets, and items sold on game sessions
            // were missed entirely.
            var seg = segment?.Trim().ToLowerInvariant();
            if (seg is "fnb" or "tcg" or "gaming" or "items")
            {
                if (seg == "gaming")
                {
                    var g = q.Where(t => t.GameId != null);
                    var gAmt = await g.SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0m;
                    var gCnt = await g.CountAsync(ct);
                    return new PeriodTotalsDto(Math.Round(gAmt, 2), gCnt, 0m, 0, Math.Round(gAmt, 2));
                }

                var rows = await q.Where(t => t.TransactionItems.Any())
                    .Select(t => new
                    {
                        t.Id, t.TotalPrice, IsGame = t.GameId != null,
                        Pct = t.Discount != null ? t.Discount.Percentage : 0,
                        Lines = t.TransactionItems.Select(ti => new
                        {
                            Full = ti.IsIncluded ? 0m : ti.Item.Price * ti.Quantity,
                            IsTcg = ti.Item.Category.ItemType == "Retail" || ti.Item.Category.Name.ToLower().Contains("tcg"),
                        }).ToList(),
                    }).ToListAsync(ct);

                decimal amount = 0m; int segCount = 0;
                foreach (var r in rows)
                {
                    var full = r.Lines.Sum(l => l.Full);
                    if (full <= 0) continue;
                    var want = seg == "items" ? full : r.Lines.Where(l => l.IsTcg == (seg == "tcg")).Sum(l => l.Full);
                    if (want <= 0) continue;
                    // Item-only invoice: share of what was paid. Game session:
                    // list price less the session's discount (TotalPrice has play time).
                    var keep = r.Pct is > 0 and < 100 ? 1m - r.Pct / 100m : 1m;
                    amount += r.IsGame ? want * keep : r.TotalPrice * (want / full);
                    segCount++;
                }
                amount = Math.Round(amount, 2);
                return new PeriodTotalsDto(amount, segCount, 0m, 0, amount);
            }

            List<int> cats = new();
            if (!string.IsNullOrWhiteSpace(categoryIds))
            {
                cats = categoryIds
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                    .Where(n => n.HasValue)
                    .Select(n => n!.Value)
                    .ToList();
            }

            if (cats.Count > 0)
            {
                q = q.Where(t =>
                    (t.GameId != null && t.Game != null && cats.Contains(t.Game.CategoryId)) ||
                    (t.GameId == null && t.TransactionItems.Any(ti => ti.Item != null && cats.Contains(ti.Item.CategoryId)))
                );
            }

            var count = await q.CountAsync(ct);
            var salesAmount = await q.SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0m;

            // Paid event tickets are sales too (Rami, 2026-09) — added when
            // the view is not narrowed to specific categories.
            decimal eventsAmount = 0m; int eventsCount = 0;
            if (cats.Count == 0)
            {
                var ev = PaidEventsQuery(from, toExclusive);
                eventsAmount = await ev.SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
                eventsCount = await ev.CountAsync(ct);
            }

            return new PeriodTotalsDto(
                TotalAmount: salesAmount + eventsAmount,
                OrdersCount: count + eventsCount,
                EventsAmount: eventsAmount,
                EventsCount: eventsCount,
                SalesAmount: salesAmount
            );
        }

        public async Task<int> GetOrdersCountAsync(DateTime? from,DateTime? to,string? categoryIds,CancellationToken ct = default)
        {
            var q = _repo.Query(); // IQueryable<TransactionRecord>

            // date filter  [from .. to]
            var toExclusive = to?.Date.AddDays(1);
            if (from.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= from.Value);
            if (toExclusive.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < toExclusive.Value);

            // only completed transactions
            q = q.Where(t => t.StatusId == 6);

            // category filter (same as GetTotalsAsync)
            List<int> cats = new();
            if (!string.IsNullOrWhiteSpace(categoryIds))
            {
                cats = categoryIds
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                    .Where(n => n.HasValue)
                    .Select(n => n!.Value)
                    .ToList();
            }

            if (cats.Count > 0)
            {
                q = q.Where(t =>
                    (t.GameId != null && t.Game != null && cats.Contains(t.Game.CategoryId)) ||
                    (t.GameId == null && t.TransactionItems.Any(ti => ti.Item != null && cats.Contains(ti.Item.CategoryId)))
                );
            }

            // just count
            var count = await q.CountAsync(ct);
            return count;
        }


        public async Task<List<DailySalesDto>> GetDailySalesAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default)
        {
            // base query
            var q = _repo.Query(); // IQueryable<TransactionRecord>, AsNoTracking in repo

            q = q.Where(t => t.StatusId == 6); // only completed transactions   

            // inclusive date range [from, to]
            var toExclusive = to?.Date.AddDays(1);
            if (from.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= from.Value);
            if (toExclusive.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < toExclusive.Value);

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
                    (t.GameId != null && t.StatusId == 6 && t.Game != null && catList.Contains(t.Game.CategoryId)) ||
                    (t.GameId == null && t.TransactionItems.Any(ti => ti.Item != null && catList.Contains(ti.Item.CategoryId)))
                );
            }

            // games totals per day (use TransactionRecord.TotalPrice)
            var gamesDaily = await q.Where(t => t.GameId != null && t.StatusId ==6).GroupBy(t => (t.PaidOn ?? t.CreatedOn).Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalPrice) })
                .ToListAsync(ct);

            // items totals per day (sum Item.Price * Quantity)
            var itemsDaily = await q.Where(t => t.GameId == null && t.StatusId == 6).GroupBy(t => (t.PaidOn ?? t.CreatedOn).Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalPrice) })
                .ToListAsync(ct);

            // TCG share per day: each invoice's paid TotalPrice split by list-price
            // weight, Retail lines only (same rule as the accounting dashboard).
            var tcgRows = await q.Where(t => t.GameId == null && t.StatusId == 6
                                         && t.TransactionItems.Any(ti => ti.Item.Category.ItemType == "Retail"
                                                                      || ti.Item.Category.Name.ToLower().Contains("tcg")))
                .Select(t => new
                {
                    Day = (t.PaidOn ?? t.CreatedOn).Date,
                    t.TotalPrice,
                    Lines = t.TransactionItems.Select(ti => new
                    {
                        Full = ti.Item.Price * ti.Quantity,
                        IsTcg = ti.Item.Category.ItemType == "Retail" || ti.Item.Category.Name.ToLower().Contains("tcg"),
                    }).ToList(),
                })
                .ToListAsync(ct);
            var tcgDict = new Dictionary<DateTime, decimal>();
            foreach (var r in tcgRows)
            {
                var full = r.Lines.Sum(l => l.Full);
                if (full <= 0) continue;
                var share = r.TotalPrice * (r.Lines.Where(l => l.IsTcg).Sum(l => l.Full) / full);
                tcgDict[r.Day] = (tcgDict.TryGetValue(r.Day, out var cur) ? cur : 0m) + share;
            }

            // paid event tickets per day (confirmation date) — only when not
            // narrowed to categories, tickets carry none.
            var eventDict = new Dictionary<DateTime, decimal>();
            if (catList.Count == 0)
            {
                var evRows = await PaidEventsQuery(from, toExclusive)
                    .Select(r => new { When = r.ConfirmedOn ?? r.CreatedOn, r.Amount })
                    .ToListAsync(ct);
                foreach (var g in evRows.GroupBy(r => r.When.Date))
                    eventDict[g.Key] = g.Sum(r => r.Amount);
            }

            // merge
            var gameDict = gamesDaily.ToDictionary(x => x.Date, x => x.Total);
            var itemDict = itemsDaily.ToDictionary(x => x.Date, x => x.Total);
            var allDates = gameDict.Keys.Union(itemDict.Keys).Union(eventDict.Keys).OrderBy(d => d).ToList();

            var result = allDates.Select(d =>
            {
                var items = itemDict.TryGetValue(d, out var it) ? it : 0m;
                var games = gameDict.TryGetValue(d, out var gt) ? gt : 0m;
                var events = eventDict.TryGetValue(d, out var et) ? et : 0m;
                return new DailySalesDto(
                    Date: d,
                    ItemsTotal: items,
                    GamesTotal: games,
                    GrandTotal: items + games + events,
                    EventsTotal: events,
                    TcgTotal: Math.Round(tcgDict.TryGetValue(d, out var tc) ? tc : 0m, 2)
                );
            }).ToList();

            // zero-fill missing days if range provided
            if (from.HasValue && toExclusive.HasValue)
            {
                var start = from.Value.Date;
                var endEx = toExclusive.Value.Date; // exclusive
                var days = Enumerable.Range(0, (endEx - start).Days).Select(i => start.AddDays(i));
                var dict = result.ToDictionary(x => x.Date);
                result = days.Select(d => dict.TryGetValue(d, out var v) ? v : new DailySalesDto(d, 0m, 0m, 0m, 0m, 0m)).ToList();
            }

            return result;
        }

        public async Task<List<ItemSalesReportDto>> GetItemSalesReportAsync(
    DateTime? from,
    DateTime? to,
    string? categoryIds,
    int top,
    CancellationToken ct = default)
        {
            var q = _repo.Query(); // IQueryable<TransactionRecord>

            // Date range [from..to]
            var toExclusive = to?.Date.AddDays(1);
            if (from.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= from.Value);
            if (toExclusive.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < toExclusive.Value);

            // Only completed transactions
            q = q.Where(t => t.StatusId == 6);

            // Only item transactions (no games)
            q = q.Where(t => t.GameId == null);

            // Parse categoryIds: "1,2,3"
            List<int> cats = new();
            if (!string.IsNullOrWhiteSpace(categoryIds))
            {
                cats = categoryIds
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                    .Where(n => n.HasValue)
                    .Select(n => n!.Value)
                    .ToList();
            }

            // Flatten items (only scalar properties!)
            var itemsQuery = q
                .SelectMany(t => t.TransactionItems
                    .Where(ti => ti.Item != null)
                    .Select(ti => new
                    {
                        TransactionId = t.Id,
                        ItemId = ti.Item.Id,
                        ItemName = ti.Item.Name,
                        CategoryId = ti.Item.CategoryId,
                        ItemType = ti.Item.Type,
                        ItemPrice = ti.Item.Price,
                        Quantity = ti.Quantity
                    }));

            if (cats.Count > 0)
            {
                itemsQuery = itemsQuery.Where(x => cats.Contains(x.CategoryId));
            }

            var grouped = await itemsQuery
                .GroupBy(x => new
                {
                    x.ItemId,
                    x.ItemName,
                    x.CategoryId,
                    x.ItemType
                })
                .Select(g => new ItemSalesReportDto
                {
                    ItemId = g.Key.ItemId,
                    ItemName = g.Key.ItemName,
                    CategoryId = g.Key.CategoryId,
                    // CategoryName: can't safely use navigation in grouping; fill later if needed
                    CategoryName = string.Empty,
                    ItemType = g.Key.ItemType,

                    TotalQuantity = g.Sum(x => x.Quantity),
                    TotalAmount = g.Sum(x => x.Quantity * x.ItemPrice),
                    OrdersCount = g.Select(x => x.TransactionId).Distinct().Count(),
                    AveragePerOrder = g.Select(x => x.TransactionId).Distinct().Count() == 0
                        ? 0
                        : g.Sum(x => x.Quantity) / (decimal)g.Select(x => x.TransactionId).Distinct().Count()
                })
                .OrderByDescending(x => x.TotalQuantity) // or .OrderByDescending(x => x.TotalAmount)
                .Take(top > 0 ? top : 100)
                .ToListAsync(ct);

            // Fill category name + TCG flag from the categories table (one query).
            var catIds = grouped.Select(g => g.CategoryId).Distinct().ToList();
            var catInfo = await _repoItem.Query()
                .Where(i => catIds.Contains(i.CategoryId))
                .Select(i => new { i.CategoryId, i.Category.Name, i.Category.ItemType })
                .Distinct()
                .ToListAsync(ct);
            var catMap = catInfo.GroupBy(c => c.CategoryId).ToDictionary(g => g.Key, g => g.First());
            foreach (var g in grouped)
            {
                if (!catMap.TryGetValue(g.CategoryId, out var c)) continue;
                g.CategoryName = c.Name ?? string.Empty;
                g.IsTcg = string.Equals(c.ItemType?.Trim(), "Retail", StringComparison.OrdinalIgnoreCase)
                          || (c.Name ?? "").Contains("tcg", StringComparison.OrdinalIgnoreCase);
            }

            return grouped;
        }

        public async Task<List<GameHourlySalesDto>> GetGameHourlySalesAsync(DateTime? from, DateTime? to, string? categoryIds, CancellationToken ct = default)
        {
            var q = _repo.Query(); // IQueryable<TransactionRecord>

            // date filter [from .. to]
            var toExclusive = to?.Date.AddDays(1);
            if (from.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) >= from.Value);
            if (toExclusive.HasValue) q = q.Where(t => (t.PaidOn ?? t.CreatedOn) < toExclusive.Value);

            // only completed GAME transactions
            q = q.Where(t => t.StatusId == 6 && t.GameId != null);

            // parse categoryIds (Game.CategoryId)
            List<int> cats = new();
            if (!string.IsNullOrWhiteSpace(categoryIds))
            {
                cats = categoryIds
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                    .Where(n => n.HasValue)
                    .Select(n => n!.Value)
                    .ToList();
            }

            if (cats.Count > 0)
            {
                q = q.Where(t => t.Game != null && cats.Contains(t.Game.CategoryId));
            }

            // group by CreatedOn.Hour
            var grouped = await q
                .GroupBy(t => (t.PaidOn ?? t.CreatedOn).Hour) // 0..23
                .Select(g => new GameHourlySalesDto
                {
                    Hour = g.Key,
                    SessionsCount = g.Count(),
                    TotalHours = g.Sum(x => x.Hours),
                    TotalAmount = g.Sum(x => x.TotalPrice),
                    AverageSessionHours = g.Count() == 0 ? 0 : g.Sum(x => x.Hours) / g.Count(),
                    AverageSessionAmount = g.Count() == 0 ? 0 : g.Sum(x => x.TotalPrice) / g.Count()
                })
                .OrderBy(x => x.Hour)
                .ToListAsync(ct);

            // ensure all 24 hours (0..23) exist, even if zero
            var dict = grouped.ToDictionary(x => x.Hour);
            var result = Enumerable.Range(0, 24)
                .Select(h => dict.TryGetValue(h, out var v)
                    ? v
                    : new GameHourlySalesDto
                    {
                        Hour = h,
                        SessionsCount = 0,
                        TotalHours = 0m,
                        TotalAmount = 0m,
                        AverageSessionHours = 0m,
                        AverageSessionAmount = 0m
                    })
                .OrderBy(x => x.Hour)
                .ToList();

            return result;
        }


    }
}