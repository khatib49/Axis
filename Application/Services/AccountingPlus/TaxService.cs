using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AccountingPlus
{
    public class TaxService : ITaxService
    {
        public const int PaidStatusId = 6;
        private static readonly string[] AppliesToValues = { "Sales", "Purchases", "Both" };

        private readonly ApplicationDbContext _db;

        public TaxService(ApplicationDbContext db) => _db = db;

        public async Task<List<TaxRateDto>> ListRatesAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<TaxRate>().AsNoTracking();
            if (!includeInactive) q = q.Where(t => t.IsActive);
            var rows = await q.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).ToListAsync(ct);
            return rows.Select(ToDto).ToList();
        }

        public async Task<BaseResponse<TaxRateDto>> CreateRateAsync(TaxRateUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err is not null) return new BaseResponse<TaxRateDto>(false, err, "");
            if (await _db.Set<TaxRate>().AnyAsync(t => t.Name == dto.Name.Trim(), ct))
                return new BaseResponse<TaxRateDto>(false, "A tax rate with this name already exists", "");

            var t = new TaxRate { Name = dto.Name.Trim(), RatePercent = dto.RatePercent, IsDefault = dto.IsDefault, IsActive = dto.IsActive, AppliesTo = Normalize(dto.AppliesTo) };
            if (t.IsDefault) await ClearDefaultsAsync(null, ct);
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<TaxRateDto>(true, null, "Created", ToDto(t));
        }

        public async Task<BaseResponse<TaxRateDto>> UpdateRateAsync(int id, TaxRateUpsertDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<TaxRate>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return new BaseResponse<TaxRateDto>(false, "Tax rate not found", "");
            var err = Validate(dto);
            if (err is not null) return new BaseResponse<TaxRateDto>(false, err, "");
            if (await _db.Set<TaxRate>().AnyAsync(x => x.Name == dto.Name.Trim() && x.Id != id, ct))
                return new BaseResponse<TaxRateDto>(false, "A tax rate with this name already exists", "");

            t.Name = dto.Name.Trim();
            t.RatePercent = dto.RatePercent;
            t.IsActive = dto.IsActive;
            t.AppliesTo = Normalize(dto.AppliesTo);
            if (dto.IsDefault && !t.IsDefault) await ClearDefaultsAsync(id, ct);
            t.IsDefault = dto.IsDefault;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<TaxRateDto>(true, null, "Updated", ToDto(t));
        }

        public async Task<BaseResponse> DeleteRateAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<TaxRate>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return new BaseResponse(false, "Tax rate not found");
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse(true, null);
        }

        public async Task<BaseResponse<VatReportDto>> GetVatReportAsync(DateTime from, DateTime to, bool inclusive, CancellationToken ct = default)
        {
            var start = AcctDates.StartOfDay(from);
            var end = AcctDates.EndOfDay(to);
            if (end < start) return new BaseResponse<VatReportDto>(false, "'to' must be after 'from'", "");
            if ((end - start).TotalDays > 366 * 3) return new BaseResponse<VatReportDto>(false, "Range too large (max 3 years)", "");

            var rate = await ResolveSalesRateAsync(ct);

            var sales = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.StatusId == PaidStatusId && (t.PaidOn ?? t.CreatedOn) >= start && (t.PaidOn ?? t.CreatedOn) <= end)
                .Select(t => new { Date = t.PaidOn ?? t.CreatedOn, t.TotalPrice })
                .ToListAsync(ct);

            var bills = await _db.Set<SupplierBill>().AsNoTracking()
                .Where(b => b.Status != "Void" && b.Status != "Draft" && b.BillDate >= start && b.BillDate <= end)
                .Select(b => new { b.BillDate, b.Subtotal, b.TaxAmount })
                .ToListAsync(ct);

            var rows = new List<VatReportRowDto>();
            var cursor = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (cursor <= end)
            {
                var y = cursor.Year; var m = cursor.Month;
                var gross = sales.Where(s => s.Date.Year == y && s.Date.Month == m).Sum(s => s.TotalPrice);
                var output = VatMath.Vat(gross, rate, inclusive);
                var net = VatMath.Net(gross, rate, inclusive);
                var pNet = bills.Where(b => b.BillDate.Year == y && b.BillDate.Month == m).Sum(b => b.Subtotal);
                var input = bills.Where(b => b.BillDate.Year == y && b.BillDate.Month == m).Sum(b => b.TaxAmount);
                rows.Add(new VatReportRowDto(y, m, Math.Round(gross, 2), Math.Round(net, 2), output, Math.Round(pNet, 2), Math.Round(input, 2), Math.Round(output - input, 2)));
                cursor = cursor.AddMonths(1);
            }

            var dto = new VatReportDto(start, end, rate, inclusive, rows,
                rows.Sum(r => r.OutputVat), rows.Sum(r => r.InputVat), rows.Sum(r => r.NetPayable));
            return new BaseResponse<VatReportDto>(true, null, "", dto);
        }

        private async Task<decimal> ResolveSalesRateAsync(CancellationToken ct)
        {
            var rates = await _db.Set<TaxRate>().AsNoTracking()
                .Where(t => t.IsActive && (t.AppliesTo == "Sales" || t.AppliesTo == "Both")).ToListAsync(ct);
            var pick = rates.FirstOrDefault(r => r.IsDefault) ?? rates.OrderByDescending(r => r.RatePercent).FirstOrDefault();
            return pick?.RatePercent ?? 0m;
        }

        private async Task ClearDefaultsAsync(int? exceptId, CancellationToken ct)
        {
            var others = await _db.Set<TaxRate>().Where(t => t.IsDefault && t.Id != (exceptId ?? 0)).ToListAsync(ct);
            foreach (var o in others) o.IsDefault = false;
        }

        private static string? Validate(TaxRateUpsertDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";
            if (dto.RatePercent < 0 || dto.RatePercent > 100) return "Rate must be between 0 and 100";
            if (!AppliesToValues.Contains(Normalize(dto.AppliesTo))) return "AppliesTo must be Sales, Purchases or Both";
            return null;
        }

        private static string Normalize(string? appliesTo) => appliesTo?.Trim().ToLowerInvariant() switch
        {
            "sales" => "Sales",
            "purchases" => "Purchases",
            _ => "Both",
        };

        private static TaxRateDto ToDto(TaxRate t) => new(t.Id, t.Name, t.RatePercent, t.IsDefault, t.IsActive, t.AppliesTo);
    }
}
