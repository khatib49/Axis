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
    /// Currencies + daily exchange rates. Rates are "units of currency per 1 base
    /// unit" (e.g. LBP 89500 per USD). Seeds USD (base) and LBP lazily on first use.
    /// </summary>
    public class CurrencyService : ICurrencyService
    {
        public const string DefaultBaseCode = "USD";
        public const string DefaultSecondaryCode = "LBP";
        public const decimal DefaultSecondaryRate = 89500m;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<CurrencyService> _log;

        public CurrencyService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<CurrencyService> log)
        {
            _db = db;
            _user = user;
            _log = log;
        }

        public static decimal RoundToStep(decimal amount, decimal step, int decimalPlaces)
        {
            if (step <= 0) return Math.Round(amount, decimalPlaces, MidpointRounding.AwayFromZero);
            var rounded = Math.Round(amount / step, 0, MidpointRounding.AwayFromZero) * step;
            return Math.Round(rounded, decimalPlaces, MidpointRounding.AwayFromZero);
        }

        public async Task EnsureSeededAsync(CancellationToken ct = default)
        {
            if (await _db.Set<Currency>().AnyAsync(ct)) return;

            var now = DateTime.UtcNow;
            _db.Set<Currency>().AddRange(
                new Currency { Code = DefaultBaseCode, Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBase = true, IsActive = true, RoundingStep = 0.01m },
                new Currency { Code = DefaultSecondaryCode, Name = "Lebanese Pound", Symbol = "LL", DecimalPlaces = 0, IsBase = false, IsActive = true, RoundingStep = 1000m });
            _db.Set<ExchangeRate>().Add(new ExchangeRate
            {
                CurrencyCode = DefaultSecondaryCode,
                Rate = DefaultSecondaryRate,
                EffectiveFrom = now.Date,
                CreatedOn = now,
                CreatedBy = "system",
            });
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Seeded default currencies USD (base) and LBP @ {Rate}", DefaultSecondaryRate);
        }

        public Task<Currency?> GetBaseAsync(CancellationToken ct = default)
            => _db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(c => c.IsBase, ct);

        public Task<Currency?> GetEntityAsync(string code, CancellationToken ct = default)
        {
            var c = Normalize(code);
            return _db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == c, ct);
        }

        public async Task<decimal?> GetRateAsync(string code, DateTime? asOf = null, CancellationToken ct = default)
        {
            var c = Normalize(code);
            var cur = await _db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == c, ct);
            if (cur is null) return null;
            if (cur.IsBase) return 1m;
            var at = asOf ?? DateTime.UtcNow;
            var rate = await _db.Set<ExchangeRate>().AsNoTracking()
                .Where(r => r.CurrencyCode == c && r.EffectiveFrom <= at)
                .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);
            return rate?.Rate;
        }

        public async Task<List<CurrencyDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        {
            await EnsureSeededAsync(ct);
            var q = _db.Set<Currency>().AsNoTracking();
            if (!includeInactive) q = q.Where(c => c.IsActive);
            var list = await q.OrderByDescending(c => c.IsBase).ThenBy(c => c.Code).ToListAsync(ct);
            var result = new List<CurrencyDto>();
            foreach (var c in list)
                result.Add(ToDto(c, await GetRateAsync(c.Code, null, ct)));
            return result;
        }

        public async Task<CurrencyDto?> GetAsync(string code, CancellationToken ct = default)
        {
            var c = await GetEntityAsync(code, ct);
            return c is null ? null : ToDto(c, await GetRateAsync(c.Code, null, ct));
        }

        public async Task<BaseResponse<CurrencyDto>> CreateAsync(CurrencyCreateDto dto, CancellationToken ct = default)
        {
            await EnsureSeededAsync(ct);
            var code = Normalize(dto.Code);
            if (code.Length != 3) return Fail<CurrencyDto>("Code must be 3 letters.");
            if (string.IsNullOrWhiteSpace(dto.Name)) return Fail<CurrencyDto>("Name is required.");
            if (dto.DecimalPlaces < 0 || dto.DecimalPlaces > 6) return Fail<CurrencyDto>("DecimalPlaces must be 0-6.");
            if (dto.RoundingStep < 0) return Fail<CurrencyDto>("RoundingStep cannot be negative.");
            if (await _db.Set<Currency>().AnyAsync(c => c.Code == code, ct)) return Fail<CurrencyDto>($"Currency {code} already exists.");
            if (dto.IsBase && await _db.Set<Currency>().AnyAsync(c => c.IsBase, ct)) return Fail<CurrencyDto>("A base currency already exists.");
            if (!dto.IsBase && (dto.InitialRate is null || dto.InitialRate <= 0)) return Fail<CurrencyDto>("InitialRate (units per 1 base) is required for a non-base currency.");

            var e = new Currency
            {
                Code = code,
                Name = dto.Name.Trim(),
                Symbol = string.IsNullOrWhiteSpace(dto.Symbol) ? code : dto.Symbol.Trim(),
                DecimalPlaces = dto.DecimalPlaces,
                IsBase = dto.IsBase,
                IsActive = dto.IsActive,
                RoundingStep = dto.RoundingStep,
            };
            _db.Set<Currency>().Add(e);
            if (!dto.IsBase)
            {
                _db.Set<ExchangeRate>().Add(new ExchangeRate
                {
                    CurrencyCode = code,
                    Rate = dto.InitialRate!.Value,
                    EffectiveFrom = DateTime.UtcNow.Date,
                    CreatedOn = DateTime.UtcNow,
                    CreatedBy = _user.UserName,
                });
            }
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(e, dto.IsBase ? 1m : dto.InitialRate), "Currency created.");
        }

        public async Task<BaseResponse<CurrencyDto>> UpdateAsync(string code, CurrencyUpdateDto dto, CancellationToken ct = default)
        {
            var c = Normalize(code);
            var e = await _db.Set<Currency>().FirstOrDefaultAsync(x => x.Code == c, ct);
            if (e is null) return Fail<CurrencyDto>("Currency not found.");
            if (dto.Name != null) e.Name = dto.Name.Trim();
            if (dto.Symbol != null) e.Symbol = dto.Symbol.Trim();
            if (dto.DecimalPlaces.HasValue) e.DecimalPlaces = Math.Clamp(dto.DecimalPlaces.Value, 0, 6);
            if (dto.RoundingStep.HasValue && dto.RoundingStep.Value >= 0) e.RoundingStep = dto.RoundingStep.Value;
            if (dto.IsActive.HasValue)
            {
                if (e.IsBase && !dto.IsActive.Value) return Fail<CurrencyDto>("The base currency cannot be deactivated.");
                e.IsActive = dto.IsActive.Value;
            }
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(e, await GetRateAsync(e.Code, null, ct)), "Currency updated.");
        }

        public async Task<BaseResponse<ExchangeRateDto>> SetRateAsync(SetRateDto dto, CancellationToken ct = default)
        {
            await EnsureSeededAsync(ct);
            var code = Normalize(dto.CurrencyCode);
            var cur = await _db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (cur is null) return Fail<ExchangeRateDto>("Currency not found.");
            if (cur.IsBase) return Fail<ExchangeRateDto>("The base currency has no rate (always 1).");
            if (dto.Rate <= 0) return Fail<ExchangeRateDto>("Rate must be greater than zero.");

            var effective = (dto.EffectiveFrom ?? DateTime.UtcNow).ToUniversalTime().Date;
            var e = new ExchangeRate
            {
                CurrencyCode = code,
                Rate = dto.Rate,
                EffectiveFrom = effective,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
            };
            _db.Set<ExchangeRate>().Add(e);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Rate {Code}={Rate} effective {Date} set by {User}", code, dto.Rate, effective, _user.UserName);
            return Ok(ToDto(e), "Rate saved.");
        }

        public async Task<List<ExchangeRateDto>> HistoryAsync(string code, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        {
            var c = Normalize(code);
            var q = _db.Set<ExchangeRate>().AsNoTracking().Where(r => r.CurrencyCode == c);
            if (from.HasValue) q = q.Where(r => r.EffectiveFrom >= from.Value);
            if (to.HasValue) q = q.Where(r => r.EffectiveFrom <= to.Value);
            var list = await q.OrderBy(r => r.EffectiveFrom).ThenBy(r => r.Id).ToListAsync(ct);
            return list.Select(ToDto).ToList();
        }

        public async Task<BaseResponse<ConversionResultDto>> ConvertAsync(decimal amount, string from, string to, CancellationToken ct = default)
        {
            await EnsureSeededAsync(ct);
            var fromCur = await GetEntityAsync(from, ct);
            var toCur = await GetEntityAsync(to, ct);
            if (fromCur is null) return Fail<ConversionResultDto>($"Unknown currency '{from}'.");
            if (toCur is null) return Fail<ConversionResultDto>($"Unknown currency '{to}'.");
            var rateFrom = await GetRateAsync(fromCur.Code, null, ct);
            var rateTo = await GetRateAsync(toCur.Code, null, ct);
            if (rateFrom is null) return Fail<ConversionResultDto>($"No rate set for {fromCur.Code}.");
            if (rateTo is null) return Fail<ConversionResultDto>($"No rate set for {toCur.Code}.");

            var inBase = amount / rateFrom.Value;
            var raw = inBase * rateTo.Value;
            var result = toCur.IsBase
                ? Math.Round(raw, toCur.DecimalPlaces, MidpointRounding.AwayFromZero)
                : RoundToStep(raw, toCur.RoundingStep, toCur.DecimalPlaces);
            return Ok(new ConversionResultDto(amount, fromCur.Code, toCur.Code, result, rateFrom.Value, rateTo.Value, toCur.RoundingStep), "OK");
        }

        private static string Normalize(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
        private static CurrencyDto ToDto(Currency c, decimal? rate)
            => new(c.Code, c.Name, c.Symbol, c.DecimalPlaces, c.IsBase, c.IsActive, c.RoundingStep, rate);
        private static ExchangeRateDto ToDto(ExchangeRate r)
            => new(r.Id, r.CurrencyCode, r.Rate, r.EffectiveFrom, r.CreatedOn, r.CreatedBy);
        private static BaseResponse<T> Fail<T>(string msg) => new(false, msg, msg);
        private static BaseResponse<T> Ok<T>(T data, string msg) => new(true, null, msg, data);
    }
}
