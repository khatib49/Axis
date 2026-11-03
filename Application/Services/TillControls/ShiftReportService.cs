using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.TillControls
{
    /// <summary>
    /// X/Z report maths. Sales by method come from PaymentTenders of the shift;
    /// transaction counts come from paid (status 6) transactions the shift's user
    /// created, paid inside the shift window. Expected cash =
    /// float + cash tenders (in base) + pay-ins − pay-outs.
    /// </summary>
    public class ShiftReportService : IShiftReportService
    {
        public const int PaidStatusId = 6;
        private readonly ApplicationDbContext _db;

        public ShiftReportService(ApplicationDbContext db) => _db = db;

        public async Task<ShiftReportDto> BuildAsync(CashierShift shift, string reportType, CancellationToken ct = default)
        {
            var start = shift.OpenedOn;
            var end = shift.ClosedOn ?? DateTime.UtcNow;

            var tenders = await _db.Set<PaymentTender>().AsNoTracking()
                .Where(t => t.ShiftId == shift.Id).ToListAsync(ct);

            var byMethod = tenders
                .GroupBy(t => t.Method)
                .Select(g => new ShiftMethodTotalDto(g.Key, g.Sum(x => x.AmountInBase), g.Count()))
                .OrderBy(x => x.Method).ToList();

            var byCurrency = tenders
                .GroupBy(t => t.CurrencyCode)
                .Select(g => new ShiftCurrencyTotalDto(g.Key, g.Sum(x => x.AmountInCurrency), g.Sum(x => x.AmountInBase), g.Count()))
                .OrderBy(x => x.CurrencyCode).ToList();

            var tenderedTotal = tenders.Sum(t => t.AmountInBase);
            var cashTendered = tenders.Where(t => t.Method == TenderMethods.Cash).Sum(t => t.AmountInBase);

            var txQuery = _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.StatusId == PaidStatusId
                         && t.PaidOn != null && t.PaidOn >= start && t.PaidOn <= end
                         && t.CreatedBy == shift.UserName);
            var txCount = await txQuery.CountAsync(ct);
            var txTotal = txCount == 0 ? 0m : await txQuery.SumAsync(t => t.TotalPrice, ct);

            var movements = await _db.Set<CashMovement>().AsNoTracking()
                .Where(m => m.ShiftId == shift.Id).ToListAsync(ct);
            var payIns = movements.Where(m => m.Type == CashMovementTypes.PayIn).ToList();
            var payOuts = movements.Where(m => m.Type == CashMovementTypes.PayOut).ToList();

            var voids = await _db.Set<VoidRequest>().AsNoTracking()
                .Where(v => v.RequestedBy == shift.UserName && v.RequestedOn >= start && v.RequestedOn <= end)
                .ToListAsync(ct);
            var voidsPending = voids.Count(v => v.Status == VoidRequestStatuses.Pending);
            var voidsApproved = voids.Where(v => v.Type == VoidRequestTypes.Void && v.Status == VoidRequestStatuses.Approved).ToList();
            var refundsApproved = voids.Where(v => v.Type == VoidRequestTypes.Refund && v.Status == VoidRequestStatuses.Approved).ToList();

            var expectedCash = shift.OpeningFloat + cashTendered + payIns.Sum(m => m.Amount) - payOuts.Sum(m => m.Amount);
            var counted = reportType == "Z" ? shift.CountedCash : null;
            var variance = reportType == "Z" && counted.HasValue ? counted.Value - expectedCash : (decimal?)null;

            return new ShiftReportDto(
                reportType, shift.Id, shift.RegisterName, shift.UserName,
                shift.OpenedOn, shift.ClosedOn, DateTime.UtcNow,
                shift.OpeningFloat,
                byMethod, byCurrency, tenderedTotal, cashTendered,
                txCount, txTotal,
                payIns.Count, payIns.Sum(m => m.Amount),
                payOuts.Count, payOuts.Sum(m => m.Amount),
                voidsPending, voidsApproved.Count, voidsApproved.Sum(v => v.Amount ?? 0m),
                refundsApproved.Count, refundsApproved.Sum(v => v.Amount ?? 0m),
                expectedCash, counted, variance);
        }
    }
}
