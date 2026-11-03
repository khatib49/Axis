using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Floor
{
    public class FloorBillingService : IFloorBillingService
    {
        private static readonly string[] Methods = { "Cash", "Card", "Wallet", "Other" };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<FloorBillingService> _logger;

        public FloorBillingService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<FloorBillingService> logger)
        {
            _db = db;
            _user = user;
            _logger = logger;
        }

        // ── Courses ──────────────────────────────────────────────────
        public async Task<List<OrderCourseDto>> ListCoursesAsync(int transactionId, CancellationToken ct = default)
        {
            var lines = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(l => l.TransactionRecordId == transactionId)
                .Select(l => new { l.ItemId, ItemName = l.Item.Name, l.Quantity })
                .ToListAsync(ct);
            var courses = await _db.Set<OrderCourse>().AsNoTracking()
                .Where(c => c.TransactionId == transactionId).ToListAsync(ct);

            // Every line shows up; lines without an explicit course default to course 1 (Id 0 = not persisted yet).
            return lines.Select(l =>
            {
                var c = courses.Where(x => x.ItemId == l.ItemId).OrderBy(x => x.SeatNumber ?? 0).FirstOrDefault();
                return new OrderCourseDto(c?.Id ?? 0, transactionId, l.ItemId, l.ItemName, l.Quantity, c?.CourseNumber ?? 1, c?.FiredOn, c?.FiredBy);
            }).OrderBy(c => c.CourseNumber).ThenBy(c => c.ItemName).ToList();
        }

        public async Task<BaseResponse<List<OrderCourseDto>>> SetCoursesAsync(int transactionId, SetCoursesRequestDto dto, CancellationToken ct = default)
        {
            if (dto.Assignments is null || dto.Assignments.Count == 0)
                return new BaseResponse<List<OrderCourseDto>>(false, "Validation", "No course assignments provided.");
            if (dto.Assignments.Any(a => a.CourseNumber < 1 || a.CourseNumber > 20))
                return new BaseResponse<List<OrderCourseDto>>(false, "Validation", "Course number must be between 1 and 20.");
            if (!await _db.Set<TransactionRecord>().AnyAsync(t => t.Id == transactionId, ct))
                return new BaseResponse<List<OrderCourseDto>>(false, "Not found", "Invoice not found.");

            var lineQty = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(l => l.TransactionRecordId == transactionId).ToDictionaryAsync(l => l.ItemId, l => l.Quantity, ct);
            var unknown = dto.Assignments.Select(a => a.ItemId).Except(lineQty.Keys).ToList();
            if (unknown.Count > 0)
                return new BaseResponse<List<OrderCourseDto>>(false, "Validation", $"Items not on this invoice: {string.Join(", ", unknown)}");

            var existing = await _db.Set<OrderCourse>().Where(c => c.TransactionId == transactionId).ToListAsync(ct);
            foreach (var a in dto.Assignments.GroupBy(a => a.ItemId).Select(g => g.Last()))
            {
                var rows = existing.Where(c => c.ItemId == a.ItemId).ToList();
                if (rows.Count == 0)
                    _db.Add(new OrderCourse { TransactionId = transactionId, ItemId = a.ItemId, CourseNumber = a.CourseNumber, Quantity = lineQty[a.ItemId] });
                // A line split over several seats keeps one course for all its portions.
                foreach (var row in rows.Where(r => r.CourseNumber != a.CourseNumber))
                {
                    row.CourseNumber = a.CourseNumber;
                    // Moving a line to another course puts it back in the "not fired" state.
                    row.FiredOn = null;
                    row.FiredBy = null;
                }
            }
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<List<OrderCourseDto>>(true, null, "Courses saved", await ListCoursesAsync(transactionId, ct));
        }

        public async Task<BaseResponse<List<OrderCourseDto>>> FireCourseAsync(int transactionId, int courseNumber, CancellationToken ct = default)
        {
            if (!await _db.Set<TransactionRecord>().AnyAsync(t => t.Id == transactionId, ct))
                return new BaseResponse<List<OrderCourseDto>>(false, "Not found", "Invoice not found.");

            var rows = await _db.Set<OrderCourse>()
                .Where(c => c.TransactionId == transactionId && c.CourseNumber == courseNumber).ToListAsync(ct);
            if (courseNumber == 1)
            {
                // Lines never assigned a course belong to course 1: materialise them so the stamp is kept.
                var assigned = await _db.Set<OrderCourse>().Where(c => c.TransactionId == transactionId).Select(c => c.ItemId).ToListAsync(ct);
                var unassigned = await _db.Set<TransactionItem>().AsNoTracking()
                    .Where(l => l.TransactionRecordId == transactionId && !assigned.Contains(l.ItemId))
                    .Select(l => new { l.ItemId, l.Quantity }).ToListAsync(ct);
                foreach (var line in unassigned)
                {
                    var row = new OrderCourse { TransactionId = transactionId, ItemId = line.ItemId, CourseNumber = 1, Quantity = line.Quantity };
                    _db.Add(row);
                    rows.Add(row);
                }
            }
            if (rows.Count == 0)
                return new BaseResponse<List<OrderCourseDto>>(false, "Validation", $"No items on course {courseNumber}.");

            var now = DateTime.UtcNow;
            var fired = 0;
            foreach (var r in rows.Where(r => r.FiredOn == null))
            {
                r.FiredOn = now;
                r.FiredBy = _user.UserName;
                fired++;
            }
            await _db.SaveChangesAsync(ct);
            // The KDS gets its tickets from the existing order pipeline at order time;
            // IKitchenBarOrderService exposes no "push" API, so firing only stamps the course.
            _logger.LogInformation("Course {Course} fired on invoice #{Trx} by {User} ({Fired} lines)", courseNumber, transactionId, _user.UserName, fired);
            return new BaseResponse<List<OrderCourseDto>>(true, null, fired == 0 ? "Course already fired" : $"Course {courseNumber} fired",
                await ListCoursesAsync(transactionId, ct));
        }


        // ── Seat lines / bill by seat ────────────────────────────────
        public async Task<BaseResponse<BillBySeatDto>> SetLinesAsync(int transactionId, SetLinesRequestDto dto, CancellationToken ct = default)
        {
            if (dto.Lines is null || dto.Lines.Count == 0)
                return new BaseResponse<BillBySeatDto>(false, "Validation", "No lines provided.");
            if (dto.Lines.Any(l => l.Quantity < 0 || (dto.Append && l.Quantity == 0)))
                return new BaseResponse<BillBySeatDto>(false, "Validation", "Quantity must be positive.");
            if (dto.Lines.Any(l => l.CourseNumber is < 1 or > 20))
                return new BaseResponse<BillBySeatDto>(false, "Validation", "Course number must be between 1 and 20.");
            if (dto.Lines.Any(l => l.SeatNumber is < 1 or > 99))
                return new BaseResponse<BillBySeatDto>(false, "Validation", "Seat number must be between 1 and 99.");
            if (!await _db.Set<TransactionRecord>().AnyAsync(t => t.Id == transactionId, ct))
                return new BaseResponse<BillBySeatDto>(false, "Not found", "Invoice not found.");

            var guestCount = await _db.Set<TableSession>().AsNoTracking()
                .Where(s => s.TransactionId == transactionId && s.Status == TableSession.StatusOpen)
                .Select(s => (int?)s.GuestCount).FirstOrDefaultAsync(ct);
            var tooHigh = dto.Lines.FirstOrDefault(l => guestCount.HasValue && l.SeatNumber > guestCount);
            if (tooHigh != null)
                return new BaseResponse<BillBySeatDto>(false, "Validation", $"Seat {tooHigh.SeatNumber} exceeds the guest count ({guestCount}).");

            var lineQty = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(l => l.TransactionRecordId == transactionId).ToDictionaryAsync(l => l.ItemId, l => l.Quantity, ct);
            var unknown = dto.Lines.Select(l => l.ItemId).Except(lineQty.Keys).Distinct().ToList();
            if (unknown.Count > 0)
                return new BaseResponse<BillBySeatDto>(false, "Validation", $"Items not on this invoice: {string.Join(", ", unknown)}");

            var rows = await _db.Set<OrderCourse>().Where(c => c.TransactionId == transactionId).ToListAsync(ct);
            var grouped = dto.Lines.GroupBy(l => new { l.ItemId, l.SeatNumber })
                .Select(g => new LineAssignmentDto(g.Key.ItemId, g.Key.SeatNumber,
                    dto.Append ? g.Sum(x => x.Quantity) : g.Last().Quantity,
                    g.Select(x => x.CourseNumber).LastOrDefault(c => c.HasValue)))
                .ToList();

            foreach (var a in grouped)
            {
                var row = rows.FirstOrDefault(r => r.ItemId == a.ItemId && r.SeatNumber == a.SeatNumber);
                // The course is shared by every portion of the line; default to what the line already has.
                var course = a.CourseNumber ?? rows.Where(r => r.ItemId == a.ItemId).Select(r => (int?)r.CourseNumber).FirstOrDefault() ?? 1;
                var qty = a.SeatNumber is null ? 0 : (dto.Append ? (row?.Quantity ?? 0) + a.Quantity : a.Quantity);

                if (a.SeatNumber.HasValue)
                {
                    var others = rows.Where(r => r.ItemId == a.ItemId && r.SeatNumber.HasValue && r.SeatNumber != a.SeatNumber).Sum(r => r.Quantity);
                    if (others + qty > lineQty[a.ItemId])
                        return new BaseResponse<BillBySeatDto>(false, "Validation",
                            $"Seat quantities for item {a.ItemId} ({others + qty}) exceed the invoice line ({lineQty[a.ItemId]}).");
                    if (qty <= 0)
                    {
                        if (row != null) { _db.Remove(row); rows.Remove(row); }
                        continue;
                    }
                }

                if (row is null)
                {
                    row = new OrderCourse { TransactionId = transactionId, ItemId = a.ItemId, SeatNumber = a.SeatNumber, Quantity = Math.Max(qty, 1), CourseNumber = course };
                    _db.Add(row);
                    rows.Add(row);
                }
                else if (a.SeatNumber.HasValue) row.Quantity = qty;

                if (a.CourseNumber.HasValue)
                {
                    foreach (var r in rows.Where(r => r.ItemId == a.ItemId && r.CourseNumber != a.CourseNumber))
                    {
                        r.CourseNumber = a.CourseNumber.Value;
                        r.FiredOn = null;
                        r.FiredBy = null;
                    }
                }
            }
            // Keep the "table" row of every touched item equal to what no seat claims.
            foreach (var itemId in grouped.Select(g => g.ItemId).Distinct())
            {
                var tableRow = rows.FirstOrDefault(r => r.ItemId == itemId && r.SeatNumber is null);
                if (tableRow != null)
                    tableRow.Quantity = Math.Max(1, lineQty[itemId] - rows.Where(r => r.ItemId == itemId && r.SeatNumber.HasValue).Sum(r => r.Quantity));
            }
            await _db.SaveChangesAsync(ct);
            return await GetBillBySeatAsync(transactionId, ct);
        }

        public async Task<BaseResponse<BillBySeatDto>> GetBillBySeatAsync(int transactionId, CancellationToken ct = default)
        {
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (trx is null) return new BaseResponse<BillBySeatDto>(false, "Not found", "Invoice not found.");
            return new BaseResponse<BillBySeatDto>(true, null, "OK", await BuildBillBySeatAsync(trx, ct));
        }

        private async Task<BillBySeatDto> BuildBillBySeatAsync(TransactionRecord trx, CancellationToken ct)
        {
            var lines = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(l => l.TransactionRecordId == trx.Id)
                .Select(l => new { l.ItemId, ItemName = l.Item.Name, Price = l.Item.Price, l.Quantity, l.IsIncluded })
                .ToListAsync(ct);
            var addOns = await _db.Set<TransactionItemAddOn>().AsNoTracking()
                .Where(a => a.TransactionRecordId == trx.Id)
                .GroupBy(a => a.ItemId).Select(g => new { ItemId = g.Key, Total = g.Sum(a => a.UnitPrice * a.Quantity) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Total, ct);
            var variants = await _db.Set<TransactionItemVariant>().AsNoTracking()
                .Where(v => v.TransactionRecordId == trx.Id)
                .GroupBy(v => v.ItemId).Select(g => new { ItemId = g.Key, Total = g.Sum(v => v.PriceDelta * v.Quantity) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Total, ct);
            var rows = await _db.Set<OrderCourse>().AsNoTracking().Where(c => c.TransactionId == trx.Id).ToListAsync(ct);
            var guestCount = await _db.Set<TableSession>().AsNoTracking()
                .Where(s => s.TransactionId == trx.Id && s.Status == TableSession.StatusOpen)
                .Select(s => (int?)s.GuestCount).FirstOrDefaultAsync(ct) ?? 0;
            decimal discountPct = 0;
            if (trx.DiscountId.HasValue)
            {
                var d = await _db.Set<Discount>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == trx.DiscountId.Value, ct);
                if (d is { IsActive: true }) discountPct = d.Percentage;
            }

            // Key 0 = the table (unassigned) group; dictionaries cannot take a null key.
            var seatLines = new Dictionary<int, List<SeatLineDto>>();
            void Add(int? seat, SeatLineDto l)
            {
                if (!seatLines.TryGetValue(seat ?? 0, out var list)) seatLines[seat ?? 0] = list = new List<SeatLineDto>();
                list.Add(l);
            }

            foreach (var l in lines)
            {
                var unit = l.IsIncluded ? 0m : l.Price;
                var extras = (addOns.TryGetValue(l.ItemId, out var a) ? a : 0m) + (variants.TryGetValue(l.ItemId, out var v) ? v : 0m);
                var extrasPerUnit = l.Quantity > 0 ? extras / l.Quantity : 0m;
                var itemRows = rows.Where(r => r.ItemId == l.ItemId).ToList();
                var tableRow = itemRows.FirstOrDefault(r => r.SeatNumber is null);
                var remaining = l.Quantity;
                foreach (var r in itemRows.Where(r => r.SeatNumber.HasValue).OrderBy(r => r.SeatNumber))
                {
                    var qty = Math.Min(r.Quantity, remaining);
                    if (qty <= 0) continue;
                    remaining -= qty;
                    Add(r.SeatNumber, new SeatLineDto(l.ItemId, l.ItemName, r.SeatNumber, qty, r.CourseNumber, unit, Math.Round(extrasPerUnit, 2),
                        Math.Round((unit + extrasPerUnit) * qty, 2), r.FiredOn));
                }
                if (remaining > 0)
                    Add(null, new SeatLineDto(l.ItemId, l.ItemName, null, remaining, tableRow?.CourseNumber ?? 1, unit, Math.Round(extrasPerUnit, 2),
                        Math.Round((unit + extrasPerUnit) * remaining, 2), tableRow?.FiredOn));
            }

            var seats = seatLines
                .OrderBy(kv => kv.Key == 0 ? 1 : 0).ThenBy(kv => kv.Key)
                .Select(kv =>
                {
                    var sub = Math.Round(kv.Value.Sum(x => x.LineTotal), 2);
                    var net = discountPct > 0 ? Math.Round(sub - sub * discountPct / 100, 2) : sub;
                    return new SeatBillDto(kv.Key == 0 ? null : kv.Key, kv.Key == 0 ? "Table" : $"Seat {kv.Key}", kv.Value, sub, net);
                }).ToList();
            var maxSeat = seats.Where(s => s.SeatNumber.HasValue).Select(s => s.SeatNumber!.Value).DefaultIfEmpty(0).Max();
            return new BillBySeatDto(trx.Id, trx.TotalPrice, discountPct, Math.Max(guestCount, maxSeat), seats,
                seats.Where(s => s.SeatNumber.HasValue).Sum(s => s.Net), seats.Where(s => !s.SeatNumber.HasValue).Sum(s => s.Net));
        }

        public async Task<BaseResponse<BillSplitListDto>> CreateSeatSplitsAsync(int transactionId, CancellationToken ct = default)
        {
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (trx is null) return new BaseResponse<BillSplitListDto>(false, "Not found", "Invoice not found.");
            var bill = await BuildBillBySeatAsync(trx, ct);
            var parts = bill.Seats.Where(s => s.Net > 0).Select(s => new { s.Label, Amount = s.Net }).ToList();
            if (parts.Count == 0) return new BaseResponse<BillSplitListDto>(false, "Validation", "No priced items on this invoice.");
            if (!bill.Seats.Any(s => s.SeatNumber.HasValue))
                return new BaseResponse<BillSplitListDto>(false, "Validation", "No item is assigned to a seat yet.");

            var existing = await _db.Set<BillSplit>().Where(s => s.TransactionId == transactionId).ToListAsync(ct);
            if (existing.Any(s => s.PaidOn != null))
                return new BaseResponse<BillSplitListDto>(false, "Validation", "Some splits are already paid; cannot re-split by seat.");
            _db.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);

            // Per-seat rounding can overshoot the invoice total by a cent or two: the last part absorbs it.
            var amounts = parts.Select(p => p.Amount).ToList();
            var overshoot = Math.Round(amounts.Sum() - trx.TotalPrice, 2);
            if (overshoot > 0) amounts[^1] = Math.Round(amounts[^1] - overshoot, 2);
            var lines = parts.Select((p, i) => new CustomSplitLineDto(amounts[i], "Cash", p.Label)).Where(l => l.Amount > 0).ToList();
            var r = await CreateCustomSplitsAsync(transactionId, new CustomSplitRequestDto(lines), ct);
            return r.Success ? new BaseResponse<BillSplitListDto>(true, null, $"Split into {lines.Count} seat bill(s)", r.Data) : r;
        }

        // ── Splits ───────────────────────────────────────────────────
        public async Task<BaseResponse<BillSplitListDto>> ListSplitsAsync(int transactionId, CancellationToken ct = default)
        {
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (trx is null) return new BaseResponse<BillSplitListDto>(false, "Not found", "Invoice not found.");
            return new BaseResponse<BillSplitListDto>(true, null, "OK", await BuildSplitListAsync(trx, ct));
        }

        public async Task<BaseResponse<BillSplitListDto>> CreateEqualSplitsAsync(int transactionId, int ways, CancellationToken ct = default)
        {
            if (ways < 2 || ways > 50) return new BaseResponse<BillSplitListDto>(false, "Validation", "Ways must be between 2 and 50.");
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (trx is null) return new BaseResponse<BillSplitListDto>(false, "Not found", "Invoice not found.");
            if (trx.TotalPrice <= 0) return new BaseResponse<BillSplitListDto>(false, "Validation", "Invoice total is zero.");

            var existing = await _db.Set<BillSplit>().Where(s => s.TransactionId == transactionId).ToListAsync(ct);
            if (existing.Any(s => s.PaidOn != null))
                return new BaseResponse<BillSplitListDto>(false, "Validation", "Some splits are already paid; cannot re-split equally.");
            _db.RemoveRange(existing);

            var share = Math.Floor(trx.TotalPrice / ways * 100) / 100;
            var remainder = Math.Round(trx.TotalPrice - share * ways, 2);
            for (var i = 1; i <= ways; i++)
            {
                _db.Add(new BillSplit
                {
                    TransactionId = transactionId,
                    SplitNumber = i,
                    Amount = i == ways ? share + remainder : share,
                    Method = "Cash",
                    BranchId = _user.BranchId,
                });
            }
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<BillSplitListDto>(true, null, $"Split {ways} ways", await BuildSplitListAsync(trx, ct));
        }

        public async Task<BaseResponse<BillSplitListDto>> CreateCustomSplitsAsync(int transactionId, CustomSplitRequestDto dto, CancellationToken ct = default)
        {
            if (dto.Splits is null || dto.Splits.Count == 0)
                return new BaseResponse<BillSplitListDto>(false, "Validation", "No split amounts provided.");
            if (dto.Splits.Any(s => s.Amount <= 0))
                return new BaseResponse<BillSplitListDto>(false, "Validation", "Every split amount must be positive.");
            var badMethod = dto.Splits.FirstOrDefault(s => s.Method != null && !Methods.Contains(s.Method));
            if (badMethod != null)
                return new BaseResponse<BillSplitListDto>(false, "Validation", $"Unknown method '{badMethod.Method}'.");

            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (trx is null) return new BaseResponse<BillSplitListDto>(false, "Not found", "Invoice not found.");

            var existing = await _db.Set<BillSplit>().AsNoTracking().Where(s => s.TransactionId == transactionId).ToListAsync(ct);
            var existingSum = existing.Sum(s => s.Amount);
            var newSum = Math.Round(dto.Splits.Sum(s => s.Amount), 2);
            if (existingSum + newSum > trx.TotalPrice + 0.005m)
                return new BaseResponse<BillSplitListDto>(false, "Validation",
                    $"Splits total {existingSum + newSum:0.00} exceeds the invoice total {trx.TotalPrice:0.00}.");

            var next = existing.Count == 0 ? 1 : existing.Max(s => s.SplitNumber) + 1;
            foreach (var s in dto.Splits)
            {
                _db.Add(new BillSplit
                {
                    TransactionId = transactionId,
                    SplitNumber = next++,
                    Amount = Math.Round(s.Amount, 2),
                    Method = s.Method ?? "Cash",
                    Note = s.Note,
                    BranchId = _user.BranchId,
                });
            }
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<BillSplitListDto>(true, null, "Splits added", await BuildSplitListAsync(trx, ct));
        }

        public async Task<BaseResponse<BillSplitListDto>> PaySplitAsync(int splitId, PaySplitRequestDto dto, CancellationToken ct = default)
        {
            var split = await _db.Set<BillSplit>().FirstOrDefaultAsync(s => s.Id == splitId, ct);
            if (split is null) return new BaseResponse<BillSplitListDto>(false, "Not found", "Split not found.");
            if (split.PaidOn != null) return new BaseResponse<BillSplitListDto>(false, "Validation", "Split is already paid.");
            if (!Methods.Contains(dto.Method)) return new BaseResponse<BillSplitListDto>(false, "Validation", $"Unknown method '{dto.Method}'.");
            split.Method = dto.Method;
            split.PaidOn = DateTime.UtcNow;
            split.PaidBy = _user.UserName;
            if (!string.IsNullOrWhiteSpace(dto.Note)) split.Note = dto.Note;
            await _db.SaveChangesAsync(ct);
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstAsync(t => t.Id == split.TransactionId, ct);
            var list = await BuildSplitListAsync(trx, ct);
            return new BaseResponse<BillSplitListDto>(true, null,
                list.Remaining <= 0 ? "All splits paid — close the invoice from the till." : "Split paid", list);
        }

        public async Task<BaseResponse<BillSplitListDto>> DeleteSplitAsync(int splitId, CancellationToken ct = default)
        {
            var split = await _db.Set<BillSplit>().FirstOrDefaultAsync(s => s.Id == splitId, ct);
            if (split is null) return new BaseResponse<BillSplitListDto>(false, "Not found", "Split not found.");
            if (split.PaidOn != null) return new BaseResponse<BillSplitListDto>(false, "Validation", "A paid split cannot be deleted.");
            _db.Remove(split);
            await _db.SaveChangesAsync(ct);
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstAsync(t => t.Id == split.TransactionId, ct);
            return new BaseResponse<BillSplitListDto>(true, null, "Split removed", await BuildSplitListAsync(trx, ct));
        }

        private async Task<BillSplitListDto> BuildSplitListAsync(TransactionRecord trx, CancellationToken ct)
        {
            var splits = await _db.Set<BillSplit>().AsNoTracking()
                .Where(s => s.TransactionId == trx.Id).OrderBy(s => s.SplitNumber).ToListAsync(ct);
            var splitTotal = splits.Sum(s => s.Amount);
            var paid = splits.Where(s => s.PaidOn != null).Sum(s => s.Amount);
            return new BillSplitListDto(trx.Id, trx.TotalPrice, splitTotal, paid, Math.Round(trx.TotalPrice - paid, 2),
                splits.Select(s => new BillSplitDto(s.Id, s.TransactionId, s.SplitNumber, s.Amount, s.Method, s.PaidOn, s.PaidBy, s.Note)).ToList());
        }

        // ── Tips ─────────────────────────────────────────────────────
        public async Task<List<TipDto>> ListTipsAsync(int transactionId, CancellationToken ct = default)
            => await _db.Set<Tip>().AsNoTracking().Where(t => t.TransactionId == transactionId).OrderBy(t => t.CreatedOn)
                .Select(t => new TipDto(t.Id, t.TransactionId, t.Amount, t.Method, t.StaffUserId, t.StaffName, t.CreatedOn, t.CreatedBy))
                .ToListAsync(ct);

        public async Task<BaseResponse<TipDto>> AddTipAsync(int transactionId, TipCreateDto dto, CancellationToken ct = default)
        {
            if (dto.Amount <= 0) return new BaseResponse<TipDto>(false, "Validation", "Tip amount must be positive.");
            if (!Methods.Contains(dto.Method)) return new BaseResponse<TipDto>(false, "Validation", $"Unknown method '{dto.Method}'.");
            if (!await _db.Set<TransactionRecord>().AnyAsync(t => t.Id == transactionId, ct))
                return new BaseResponse<TipDto>(false, "Not found", "Invoice not found.");

            var t = new Tip
            {
                TransactionId = transactionId,
                Amount = Math.Round(dto.Amount, 2),
                Method = dto.Method,
                StaffUserId = dto.StaffUserId,
                StaffName = string.IsNullOrWhiteSpace(dto.StaffName) ? null : dto.StaffName.Trim(),
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
                BranchId = _user.BranchId,
            };
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<TipDto>(true, null, "Tip added",
                new TipDto(t.Id, t.TransactionId, t.Amount, t.Method, t.StaffUserId, t.StaffName, t.CreatedOn, t.CreatedBy));
        }

        public async Task<BaseResponse<bool>> DeleteTipAsync(int tipId, CancellationToken ct = default)
        {
            var t = await _db.Set<Tip>().FirstOrDefaultAsync(x => x.Id == tipId, ct);
            if (t is null) return new BaseResponse<bool>(false, "Not found", "Tip not found.");
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<bool>(true, null, "Tip deleted", true);
        }

        public async Task<DailyTipsReportDto> DailyTipsReportAsync(DateTime day, CancellationToken ct = default)
        {
            var start = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);
            var end = start.AddDays(1);
            var tips = await _db.Set<Tip>().AsNoTracking()
                .Where(t => t.CreatedOn >= start && t.CreatedOn < end).ToListAsync(ct);
            var rows = tips
                .GroupBy(t => new { t.StaffUserId, Name = t.StaffName ?? (t.StaffUserId.HasValue ? $"User #{t.StaffUserId}" : "Unassigned") })
                .Select(g => new StaffTipsReportRowDto(g.Key.StaffUserId, g.Key.Name, g.Count(), g.Sum(t => t.Amount)))
                .OrderByDescending(r => r.Total).ToList();
            return new DailyTipsReportDto(start, tips.Sum(t => t.Amount), tips.Count, rows);
        }
    }
}
