using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Floor
{
    public class TableSessionService : ITableSessionService
    {
        private const int StatusOpenFnb = 7;
        private const int StatusPaid = 6;
        private const int FoodStatusDefault = 11;

        private readonly ApplicationDbContext _db;
        private readonly ITransactionRecordService _transactions;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<TableSessionService> _logger;

        public TableSessionService(ApplicationDbContext db, ITransactionRecordService transactions,
            ICurrentUserAccessor user, ILogger<TableSessionService> logger)
        {
            _db = db;
            _transactions = transactions;
            _user = user;
            _logger = logger;
        }

        // ── Queries ──────────────────────────────────────────────────
        public async Task<List<TableSessionDto>> ListOpenAsync(CancellationToken ct = default)
        {
            var list = await _db.Set<TableSession>().AsNoTracking().Include(s => s.Table)
                .Where(s => s.Status == TableSession.StatusOpen)
                .OrderBy(s => s.OpenedOn)
                .ToListAsync(ct);
            return list.Select(s => ToDto(s, s.Table)).ToList();
        }

        public async Task<TableSessionDto?> GetAsync(int sessionId, CancellationToken ct = default)
        {
            var s = await _db.Set<TableSession>().AsNoTracking().Include(x => x.Table).FirstOrDefaultAsync(x => x.Id == sessionId, ct);
            return s is null ? null : ToDto(s, s.Table);
        }

        public async Task<TableSessionDto?> GetByTransactionAsync(int transactionId, CancellationToken ct = default)
        {
            var s = await _db.Set<TableSession>().AsNoTracking().Include(x => x.Table)
                .Where(x => x.TransactionId == transactionId && x.Status == TableSession.StatusOpen)
                .OrderByDescending(x => x.OpenedOn)
                .FirstOrDefaultAsync(ct);
            return s is null ? null : ToDto(s, s.Table);
        }

        // ── Open ─────────────────────────────────────────────────────
        public async Task<BaseResponse<TableSessionDto>> OpenTableAsync(OpenTableRequestDto dto, CancellationToken ct = default)
        {
            var table = await _db.Set<DiningTable>().FirstOrDefaultAsync(t => t.Id == dto.TableId, ct);
            if (table is null) return Fail("Table not found.");
            if (!table.IsActive) return Fail("Table is inactive.");
            if (dto.GuestCount <= 0) return Fail("Guest count must be at least 1.");
            if (await HasOpenSessionAsync(table.Id, ct)) return Fail($"Table {table.Code} already has an open session.");

            var createdBy = _user.UserName;
            int transactionId;
            var comment = $"Table {table.Code}";

            if (dto.Items is { Count: > 0 })
            {
                // Reuse the existing F&B order pipeline (stock, add-ons, KDS, audit).
                var res = await _transactions.CreateCoffeeShopOrder(dto.UserId, 0, dto.Items, createdBy, ct,
                    comment: comment, isOpenInvoice: true);
                if (!res.Success || res.Data is null)
                    return Fail(res.Message ?? res.Error ?? "Could not open the invoice.");
                transactionId = res.Data.Id;
            }
            else
            {
                // The existing order service refuses an empty item list, so an
                // empty open tab is created directly (status 7, zero total).
                var trx = new TransactionRecord
                {
                    StatusId = StatusOpenFnb,
                    UserId = dto.UserId is > 0 ? dto.UserId : null,
                    Hours = 0,
                    TotalPrice = 0,
                    CreatedBy = createdBy,
                    CreatedOn = DateTime.UtcNow,
                    Comment = comment,
                    FK_FoodStatusId = FoodStatusDefault,
                };
                _db.Add(trx);
                await _db.SaveChangesAsync(ct);
                transactionId = trx.Id;
                AddAudit(trx.Id, "FloorOpenTable", null, null, "7", $"Empty open tab for table {table.Code}");
            }

            var session = new TableSession
            {
                TableId = table.Id,
                TransactionId = transactionId,
                GuestCount = dto.GuestCount,
                OpenedOn = DateTime.UtcNow,
                OpenedBy = createdBy,
                Status = TableSession.StatusOpen,
                Notes = dto.Notes,
                BranchId = _user.BranchId,
            };
            _db.Add(session);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Table {Code} opened by {User} on invoice #{Trx}", table.Code, createdBy, transactionId);
            return Ok(ToDto(session, table), "Table opened");
        }

        // ── Seat an existing open invoice ─────────────────────────────
        public async Task<BaseResponse<TableSessionDto>> SeatInvoiceAsync(SeatInvoiceRequestDto dto, CancellationToken ct = default)
        {
            var table = await _db.Set<DiningTable>().FirstOrDefaultAsync(t => t.Id == dto.TableId, ct);
            if (table is null) return Fail("Table not found.");
            if (!table.IsActive) return Fail("Table is inactive.");
            if (await HasOpenSessionAsync(table.Id, ct)) return Fail($"Table {table.Code} already has an open session.");

            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (trx is null) return Fail("Invoice not found.");
            if (trx.StatusId != StatusOpenFnb) return Fail("Only open F&B invoices (status 7) can be seated.");
            if (await _db.Set<TableSession>().AnyAsync(s => s.TransactionId == trx.Id && s.Status == TableSession.StatusOpen, ct))
                return Fail("This invoice is already seated at a table. Use transfer instead.");

            var session = new TableSession
            {
                TableId = table.Id,
                TransactionId = trx.Id,
                GuestCount = dto.GuestCount <= 0 ? 1 : dto.GuestCount,
                OpenedOn = DateTime.UtcNow,
                OpenedBy = _user.UserName,
                Status = TableSession.StatusOpen,
                Notes = dto.Notes,
                BranchId = _user.BranchId,
            };
            _db.Add(session);
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(session, table), "Invoice seated");
        }

        // ── Transfer ─────────────────────────────────────────────────
        public async Task<BaseResponse<TableSessionDto>> TransferAsync(int sessionId, TransferSessionRequestDto dto, CancellationToken ct = default)
        {
            var session = await _db.Set<TableSession>().Include(s => s.Table).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is null) return Fail("Session not found.");
            if (session.Status != TableSession.StatusOpen) return Fail("Only an open session can be transferred.");
            if (session.TableId == dto.TargetTableId) return Fail("Source and target table are the same.");

            var target = await _db.Set<DiningTable>().FirstOrDefaultAsync(t => t.Id == dto.TargetTableId, ct);
            if (target is null) return Fail("Target table not found.");
            if (!target.IsActive) return Fail("Target table is inactive.");
            if (await HasOpenSessionAsync(target.Id, ct)) return Fail($"Table {target.Code} already has an open session.");

            var now = DateTime.UtcNow;
            session.Status = TableSession.StatusTransferred;
            session.ClosedOn = now;

            var moved = new TableSession
            {
                TableId = target.Id,
                TransactionId = session.TransactionId,
                GuestCount = session.GuestCount,
                OpenedOn = now,
                OpenedBy = _user.UserName,
                Status = TableSession.StatusOpen,
                Notes = session.Notes,
                BranchId = session.BranchId ?? _user.BranchId,
            };
            _db.Add(moved);
            AddAudit(session.TransactionId, "FloorTransfer", "Table", session.Table.Code, target.Code,
                $"Transferred from {session.Table.Code} to {target.Code}");
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(moved, target), "Session transferred");
        }

        // ── Merge ────────────────────────────────────────────────────
        public async Task<BaseResponse<MergeResultDto>> MergeAsync(MergeInvoicesRequestDto dto, CancellationToken ct = default)
        {
            if (dto.SourceTransactionId == dto.TargetTransactionId)
                return new BaseResponse<MergeResultDto>(false, "Validation", "Source and target invoice are the same.");

            var source = await _db.Set<TransactionRecord>().Include(t => t.TransactionItems)
                .FirstOrDefaultAsync(t => t.Id == dto.SourceTransactionId, ct);
            var target = await _db.Set<TransactionRecord>().Include(t => t.TransactionItems)
                .FirstOrDefaultAsync(t => t.Id == dto.TargetTransactionId, ct);
            if (source is null) return new BaseResponse<MergeResultDto>(false, "Not found", "Source invoice not found.");
            if (target is null) return new BaseResponse<MergeResultDto>(false, "Not found", "Target invoice not found.");
            if (source.StatusId != StatusOpenFnb || target.StatusId != StatusOpenFnb)
                return new BaseResponse<MergeResultDto>(false, "Validation", "Both invoices must be open F&B invoices (status 7).");

            var sourceAddOns = await _db.Set<TransactionItemAddOn>().Where(a => a.TransactionRecordId == source.Id).ToListAsync(ct);
            var sourceVariants = await _db.Set<TransactionItemVariant>().Where(v => v.TransactionRecordId == source.Id).ToListAsync(ct);

            // 1) Move the item lines. TransactionItem has a composite key
            //    (TransactionRecordId, ItemId) so a line is re-created on the
            //    target (or folded into an existing line with the same item)
            //    and removed from the source.
            var moved = 0;
            var sourceLines = source.TransactionItems.ToList();
            foreach (var line in sourceLines)
            {
                var existing = target.TransactionItems.FirstOrDefault(l => l.ItemId == line.ItemId);
                if (existing != null)
                    existing.Quantity += line.Quantity;
                else
                {
                    var copy = new TransactionItem
                    {
                        TransactionRecordId = target.Id,
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        IsIncluded = line.IsIncluded,
                    };
                    target.TransactionItems.Add(copy);
                    _db.Add(copy);
                }
                moved++;
            }
            // Add-ons / variants keep their snapshots and simply follow the line.
            foreach (var a in sourceAddOns) a.TransactionRecordId = target.Id;
            foreach (var v in sourceVariants) v.TransactionRecordId = target.Id;
            await _db.SaveChangesAsync(ct);

            foreach (var line in sourceLines)
            {
                source.TransactionItems.Remove(line);
                _db.Remove(line);
            }

            // 2) Recompute the target total with the same per-line formula the
            //    order service uses: Σ item price × qty (skipping included
            //    lines) + Σ add-on price × qty + Σ variant delta × qty, then
            //    the invoice's own discount.
            var targetDiscount = target.DiscountId.HasValue
                ? await _db.Set<Discount>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == target.DiscountId.Value, ct)
                : null;
            target.TotalPrice = await ComputeTotalAsync(target, targetDiscount, ct);
            target.ModifiedOn = DateTime.UtcNow;

            // 3) Zero out and close the source so it does not linger as open.
            source.TotalPrice = 0;
            source.StatusId = StatusPaid;
            source.ModifiedOn = DateTime.UtcNow;
            source.Comment = string.IsNullOrWhiteSpace(source.Comment)
                ? $"Merged into #{target.Id}"
                : $"{source.Comment} | Merged into #{target.Id}";

            var sourceSession = await _db.Set<TableSession>()
                .Where(s => s.TransactionId == source.Id && s.Status == TableSession.StatusOpen).ToListAsync(ct);
            foreach (var s in sourceSession)
            {
                s.Status = TableSession.StatusMerged;
                s.ClosedOn = DateTime.UtcNow;
            }

            AddAudit(source.Id, "FloorMerge", "StatusId", "7", "6", $"Merged into #{target.Id}; {moved} line(s) moved");
            AddAudit(target.Id, "FloorMerge", "TotalPrice", null, target.TotalPrice.ToString("F2"), $"Received {moved} line(s) from #{source.Id}");
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Invoice #{Source} merged into #{Target} by {User}: {Moved} lines, new total {Total}",
                source.Id, target.Id, _user.UserName, moved, target.TotalPrice);
            return new BaseResponse<MergeResultDto>(true, null, "Merged",
                new MergeResultDto(target.Id, source.Id, target.TotalPrice, moved));
        }

        private async Task<decimal> ComputeTotalAsync(TransactionRecord trx, Discount? discount, CancellationToken ct)
        {
            var itemIds = trx.TransactionItems.Select(l => l.ItemId).Distinct().ToList();
            var prices = await _db.Set<Item>().AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Price })
                .ToDictionaryAsync(i => i.Id, i => i.Price, ct);

            decimal total = 0;
            foreach (var l in trx.TransactionItems.Where(l => !l.IsIncluded))
                total += (prices.TryGetValue(l.ItemId, out var p) ? p : 0) * l.Quantity;

            total += await _db.Set<TransactionItemAddOn>().AsNoTracking()
                .Where(a => a.TransactionRecordId == trx.Id)
                .SumAsync(a => (decimal?)(a.UnitPrice * a.Quantity), ct) ?? 0;
            total += await _db.Set<TransactionItemVariant>().AsNoTracking()
                .Where(v => v.TransactionRecordId == trx.Id)
                .SumAsync(v => (decimal?)(v.PriceDelta * v.Quantity), ct) ?? 0;

            if (discount is { IsActive: true })
            {
                total -= total * discount.Percentage / 100;
                if (total < 0) total = 0;
            }
            return Math.Round(total, 2);
        }

        // ── Close ────────────────────────────────────────────────────
        public async Task<BaseResponse<TableSessionDto>> CloseByTransactionAsync(int transactionId, CancellationToken ct = default)
        {
            var session = await _db.Set<TableSession>().Include(s => s.Table)
                .Where(s => s.TransactionId == transactionId && s.Status == TableSession.StatusOpen)
                .OrderByDescending(s => s.OpenedOn)
                .FirstOrDefaultAsync(ct);
            if (session is null) return Fail("No open session for this invoice.");
            session.Status = TableSession.StatusClosed;
            session.ClosedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(session, session.Table), "Session closed");
        }

        // ── helpers ──────────────────────────────────────────────────
        private Task<bool> HasOpenSessionAsync(int tableId, CancellationToken ct)
            => _db.Set<TableSession>().AnyAsync(s => s.TableId == tableId && s.Status == TableSession.StatusOpen, ct);

        private void AddAudit(int transactionId, string action, string? field, string? oldValue, string? newValue, string notes)
            => _db.Add(new TransactionAuditLog
            {
                TransactionId = transactionId,
                ChangedBy = _user.UserName,
                ChangedOn = DateTime.UtcNow,
                Action = action,
                FieldChanged = field,
                OldValue = oldValue,
                NewValue = newValue,
                Notes = notes,
            });

        private static BaseResponse<TableSessionDto> Fail(string message) => new(false, "Validation", message);
        private static BaseResponse<TableSessionDto> Ok(TableSessionDto dto, string message) => new(true, null, message, dto);

        private static TableSessionDto ToDto(TableSession s, DiningTable t) =>
            new(s.Id, s.TableId, t.Code, t.Name, s.TransactionId, s.GuestCount, s.OpenedOn, s.ClosedOn, s.OpenedBy, s.Status, s.Notes);
    }
}
