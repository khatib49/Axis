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
                var c = courses.FirstOrDefault(x => x.ItemId == l.ItemId);
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

            var lineItemIds = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(l => l.TransactionRecordId == transactionId).Select(l => l.ItemId).ToListAsync(ct);
            var unknown = dto.Assignments.Select(a => a.ItemId).Except(lineItemIds).ToList();
            if (unknown.Count > 0)
                return new BaseResponse<List<OrderCourseDto>>(false, "Validation", $"Items not on this invoice: {string.Join(", ", unknown)}");

            var existing = await _db.Set<OrderCourse>().Where(c => c.TransactionId == transactionId).ToListAsync(ct);
            foreach (var a in dto.Assignments.GroupBy(a => a.ItemId).Select(g => g.Last()))
            {
                var row = existing.FirstOrDefault(c => c.ItemId == a.ItemId);
                if (row is null)
                    _db.Add(new OrderCourse { TransactionId = transactionId, ItemId = a.ItemId, CourseNumber = a.CourseNumber });
                else if (row.CourseNumber != a.CourseNumber)
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
                    .Select(l => l.ItemId).ToListAsync(ct);
                foreach (var itemId in unassigned)
                {
                    var row = new OrderCourse { TransactionId = transactionId, ItemId = itemId, CourseNumber = 1 };
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
