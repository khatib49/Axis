using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.TillControls
{
    /// <summary>
    /// Multi-currency tenders against a transaction. Each tender is converted to
    /// the base currency with the rate in force at the time; the summary compares
    /// the paid base amount with TransactionRecord.TotalPrice and offers the change
    /// in every active currency (rounded to that currency's cash step).
    /// </summary>
    public class TenderService : ITenderService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ICurrencyService _currency;
        private readonly ILogger<TenderService> _log;

        public TenderService(ApplicationDbContext db, ICurrentUserAccessor user, ICurrencyService currency, ILogger<TenderService> log)
        {
            _db = db;
            _user = user;
            _currency = currency;
            _log = log;
        }

        public async Task<BaseResponse<TenderSummaryDto>> AddAsync(AddTendersDto dto, CancellationToken ct = default)
        {
            if (dto.Tenders is null || dto.Tenders.Count == 0) return Fail<TenderSummaryDto>("At least one tender line is required.");
            await _currency.EnsureSeededAsync(ct);
            var tx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (tx is null) return Fail<TenderSummaryDto>($"Transaction #{dto.TransactionId} not found.");

            var userId = _user.UserId;
            var shift = userId is null ? null : await _db.Set<CashierShift>().AsNoTracking()
                .Where(s => s.UserId == userId.Value && s.Status == ShiftStatuses.Open)
                .OrderByDescending(s => s.OpenedOn).FirstOrDefaultAsync(ct);

            var now = DateTime.UtcNow;
            var entities = new List<PaymentTender>();
            foreach (var line in dto.Tenders)
            {
                if (line.Amount <= 0) return Fail<TenderSummaryDto>("Tender amounts must be greater than zero.");
                var method = TenderMethods.All.FirstOrDefault(m => string.Equals(m, line.Method, StringComparison.OrdinalIgnoreCase));
                if (method is null) return Fail<TenderSummaryDto>($"Unknown payment method '{line.Method}'.");
                var cur = await _currency.GetEntityAsync(line.CurrencyCode, ct);
                if (cur is null || !cur.IsActive) return Fail<TenderSummaryDto>($"Unknown or inactive currency '{line.CurrencyCode}'.");
                var rate = await _currency.GetRateAsync(cur.Code, null, ct);
                if (rate is null || rate <= 0) return Fail<TenderSummaryDto>($"No exchange rate set for {cur.Code}.");

                entities.Add(new PaymentTender
                {
                    TransactionId = tx.Id,
                    ShiftId = shift?.Id,
                    CurrencyCode = cur.Code,
                    AmountInCurrency = Math.Round(line.Amount, cur.DecimalPlaces, MidpointRounding.AwayFromZero),
                    RateUsed = rate.Value,
                    AmountInBase = Math.Round(line.Amount / rate.Value, 2, MidpointRounding.AwayFromZero),
                    Method = method,
                    Reference = string.IsNullOrWhiteSpace(line.Reference) ? null : line.Reference.Trim(),
                    CreatedOn = now,
                    CreatedBy = _user.UserName,
                    BranchId = _user.BranchId,
                });
            }

            _db.Set<PaymentTender>().AddRange(entities);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("{Count} tender(s) added to tx {Tx} by {User} (shift {Shift})", entities.Count, tx.Id, _user.UserName, shift?.Id);
            return await SummaryAsync(tx.Id, ct);
        }

        public async Task<BaseResponse<TenderSummaryDto>> SummaryAsync(int transactionId, CancellationToken ct = default)
        {
            await _currency.EnsureSeededAsync(ct);
            var tx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (tx is null) return Fail<TenderSummaryDto>($"Transaction #{transactionId} not found.");
            var baseCur = await _currency.GetBaseAsync(ct);
            var baseCode = baseCur?.Code ?? CurrencyService.DefaultBaseCode;

            var tenders = await _db.Set<PaymentTender>().AsNoTracking()
                .Where(t => t.TransactionId == transactionId).OrderBy(t => t.CreatedOn).ThenBy(t => t.Id).ToListAsync(ct);
            var paid = tenders.Sum(t => t.AmountInBase);
            var remaining = Math.Max(0m, Math.Round(tx.TotalPrice - paid, 2));
            var change = Math.Max(0m, Math.Round(paid - tx.TotalPrice, 2));

            var changeBy = new List<ChangeDueDto>();
            foreach (var cur in await _currency.ListAsync(false, ct))
            {
                if (cur.IsBase) { changeBy.Add(new ChangeDueDto(cur.Code, change)); continue; }
                if (cur.CurrentRate is null) continue;
                changeBy.Add(new ChangeDueDto(cur.Code, CurrencyService.RoundToStep(change * cur.CurrentRate.Value, cur.RoundingStep, cur.DecimalPlaces)));
            }

            return Ok(new TenderSummaryDto(transactionId, baseCode, tx.TotalPrice, paid, remaining, change, changeBy,
                tenders.Select(ToDto).ToList()), "OK");
        }

        public async Task<List<PaymentTenderDto>> ListByShiftAsync(int shiftId, CancellationToken ct = default)
        {
            var list = await _db.Set<PaymentTender>().AsNoTracking()
                .Where(t => t.ShiftId == shiftId).OrderBy(t => t.CreatedOn).ThenBy(t => t.Id).ToListAsync(ct);
            return list.Select(ToDto).ToList();
        }

        public async Task<BaseResponse<TenderSummaryDto>> DeleteAsync(int tenderId, CancellationToken ct = default)
        {
            var e = await _db.Set<PaymentTender>().FirstOrDefaultAsync(t => t.Id == tenderId, ct);
            if (e is null) return Fail<TenderSummaryDto>("Tender not found.");
            if (e.ShiftId.HasValue)
            {
                var shift = await _db.Set<CashierShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == e.ShiftId.Value, ct);
                if (shift is not null && shift.Status != ShiftStatuses.Open)
                    return Fail<TenderSummaryDto>("The shift this tender belongs to is closed; it can no longer be changed.");
            }
            if (e.CreatedBy != _user.UserName && !_user.IsInRole("admin"))
                return Fail<TenderSummaryDto>("Only the cashier who entered the tender (or an admin) can remove it.");
            _db.Set<PaymentTender>().Remove(e);
            await _db.SaveChangesAsync(ct);
            return await SummaryAsync(e.TransactionId, ct);
        }

        private static PaymentTenderDto ToDto(PaymentTender t) => new(
            t.Id, t.TransactionId, t.ShiftId, t.CurrencyCode, t.AmountInCurrency, t.RateUsed, t.AmountInBase,
            t.Method, t.Reference, t.CreatedOn, t.CreatedBy);
        private static BaseResponse<T> Fail<T>(string msg) => new(false, msg, msg);
        private static BaseResponse<T> Ok<T>(T data, string msg) => new(true, null, msg, data);
    }
}
