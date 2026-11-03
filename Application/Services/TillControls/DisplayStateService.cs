using System.Text.Json;
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
    /// Customer-facing display state per register. The till pushes the cart
    /// (totals recomputed in USD and LBP at today's rate, LBP rounded to the
    /// currency's cash step); the screen polls the view with its token.
    /// </summary>
    public class DisplayStateService : IDisplayStateService
    {
        public const string SecondaryCode = "LBP";
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly ICurrencyService _currency;
        private readonly IWhishInvoicePaymentService _whish;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<DisplayStateService> _log;

        public DisplayStateService(ApplicationDbContext db, ICurrencyService currency, IWhishInvoicePaymentService whish, ICurrentUserAccessor user, ILogger<DisplayStateService> log)
        {
            _db = db; _currency = currency; _whish = whish; _user = user; _log = log;
        }

        private static string NewToken() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..16];
        private static string NormalizeRegister(string s) => (s ?? string.Empty).Trim();

        /// <summary>Pure totals math so it can be unit-tested: USD rounded to cents, LBP = USD × rate rounded to the cash step.</summary>
        public static (decimal totalUsd, decimal totalLbp) ComputeTotals(IEnumerable<DisplayCartLineDto> cart, decimal rateLbp, decimal roundingStep, int decimalPlaces)
        {
            var usd = Math.Round(cart.Sum(l => l.Qty * l.Price), 2, MidpointRounding.AwayFromZero);
            var lbp = rateLbp <= 0 ? 0m : CurrencyService.RoundToStep(usd * rateLbp, roundingStep, decimalPlaces);
            return (usd, lbp);
        }

        public async Task<List<DisplayStateDto>> ListAsync(CancellationToken ct = default)
            => (await _db.Set<DisplayState>().AsNoTracking().OrderBy(d => d.RegisterName).ToListAsync(ct)).Select(ToDto).ToList();

        public async Task<BaseResponse<DisplayStateDto>> CreateAsync(DisplayCreateDto dto, CancellationToken ct = default)
        {
            var name = NormalizeRegister(dto?.RegisterName ?? string.Empty);
            if (name.Length == 0) return Fail<DisplayStateDto>("Validation", "Register name is required.");
            if (name.Length > 60) return Fail<DisplayStateDto>("Validation", "Register name is too long (60 max).");
            if (await _db.Set<DisplayState>().AnyAsync(d => d.RegisterName == name, ct))
                return Fail<DisplayStateDto>("Duplicate", $"A display already exists for register '{name}'.");
            var d = new DisplayState { RegisterName = name, Token = NewToken(), BranchId = _user.BranchId, UpdatedOn = DateTime.UtcNow };
            _db.Set<DisplayState>().Add(d);
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(d), "Display created.");
        }

        public async Task<BaseResponse<DisplayStateDto>> RotateTokenAsync(int id, CancellationToken ct = default)
        {
            var d = await _db.Set<DisplayState>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return Fail<DisplayStateDto>("NotFound", "Display not found.");
            d.Token = NewToken();
            d.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(d), "Token rotated.");
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var d = await _db.Set<DisplayState>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return false;
            _db.Set<DisplayState>().Remove(d);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        private async Task<DisplayState?> FindOrCreateAsync(string registerName, CancellationToken ct)
        {
            var name = NormalizeRegister(registerName);
            if (name.Length == 0 || name.Length > 60) return null;
            var d = await _db.Set<DisplayState>().FirstOrDefaultAsync(x => x.RegisterName == name, ct);
            if (d is null)
            {
                d = new DisplayState { RegisterName = name, Token = NewToken(), BranchId = _user.BranchId, UpdatedOn = DateTime.UtcNow };
                _db.Set<DisplayState>().Add(d);
                await _db.SaveChangesAsync(ct);
                _log.LogInformation("Display auto-created for register {Register} by {User}", name, _user.UserName);
            }
            return d;
        }

        public async Task<BaseResponse<DisplayStateDto>> PushAsync(string registerName, DisplayPushDto dto, CancellationToken ct = default)
        {
            var d = await FindOrCreateAsync(registerName, ct);
            if (d is null) return Fail<DisplayStateDto>("Validation", "Register name is required (60 chars max).");

            var cart = (dto?.Cart ?? new List<DisplayCartLineDto>())
                .Where(l => l is not null && l.Qty > 0)
                .Select(l => new DisplayCartLineDto((l.Name ?? string.Empty).Trim(), l.Qty, l.Price))
                .ToList();
            var status = DisplayStatuses.All.FirstOrDefault(s => string.Equals(s, dto?.Status, StringComparison.OrdinalIgnoreCase))
                         ?? (cart.Count == 0 ? DisplayStatuses.Idle : DisplayStatuses.Cart);

            await _currency.EnsureSeededAsync(ct);
            var lbp = await _currency.GetEntityAsync(SecondaryCode, ct);
            var rate = await _currency.GetRateAsync(SecondaryCode, null, ct) ?? 0m;
            var (usd, totalLbp) = ComputeTotals(cart, rate, lbp?.RoundingStep ?? 1000m, lbp?.DecimalPlaces ?? 0);

            d.TransactionId = dto?.TransactionId;
            d.CartJson = JsonSerializer.Serialize(cart, JsonOpts);
            d.TotalUsd = usd;
            d.RateLbp = rate;
            d.TotalLbp = totalLbp;
            d.Status = status;
            if (status != DisplayStatuses.Awaiting && status != DisplayStatuses.Paid) d.WhishRequestId = null;
            d.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(d), "OK");
        }

        public async Task<BaseResponse<DisplayViewDto>> ViewAsync(string token, CancellationToken ct = default)
        {
            var t = (token ?? string.Empty).Trim();
            if (t.Length == 0) return Fail<DisplayViewDto>("Unauthorized", "Missing display token.");
            var d = await _db.Set<DisplayState>().FirstOrDefaultAsync(x => x.Token == t, ct);
            if (d is null) return Fail<DisplayViewDto>("Unauthorized", "Unknown display token.");

            string? qr = null;
            decimal? whishAmount = null;
            if (d.WhishRequestId is int rid && (d.Status == DisplayStatuses.Awaiting || d.Status == DisplayStatuses.Paid))
            {
                var check = await _whish.CheckAsync(rid, ct);
                if (check.Success && check.Data is not null)
                {
                    whishAmount = check.Data.Amount;
                    if (check.Data.Status == WhishRequestStatuses.Paid && d.Status == DisplayStatuses.Awaiting)
                    {
                        d.Status = DisplayStatuses.Paid;
                        d.UpdatedOn = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                    }
                    else if (d.Status == DisplayStatuses.Awaiting && (check.Data.Status == WhishRequestStatuses.Failed || check.Data.Status == WhishRequestStatuses.Expired))
                    {
                        d.Status = DisplayStatuses.Cart;
                        d.WhishRequestId = null;
                        d.UpdatedOn = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                    }
                    if (d.Status == DisplayStatuses.Awaiting) qr = check.Data.CollectUrl;
                }
            }

            return Ok(new DisplayViewDto(d.RegisterName, d.Status, d.TransactionId, ParseCart(d.CartJson), d.TotalUsd, d.RateLbp, d.TotalLbp, qr, whishAmount, d.UpdatedOn), "OK");
        }

        public async Task<BaseResponse<WhishStartResultDto>> RequestWhishAsync(string registerName, DisplayRequestWhishDto dto, string successUrl, string cancelUrl, CancellationToken ct = default)
        {
            var d = await FindOrCreateAsync(registerName, ct);
            if (d is null) return Fail<WhishStartResultDto>("Validation", "Register name is required (60 chars max).");
            var txId = dto?.TransactionId ?? d.TransactionId;
            if (txId is null) return Fail<WhishStartResultDto>("NoInvoice", "No invoice is on this display yet. Push the cart with its invoice id first.");

            var start = await _whish.StartAsync(txId.Value, WhishRequestSources.Display, successUrl, cancelUrl, dto?.ReturnUrl, ct);
            if (!start.Success || start.Data is null) return start;

            d.TransactionId = txId;
            d.WhishRequestId = start.Data.RequestId;
            d.Status = DisplayStatuses.Awaiting;
            d.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return start;
        }

        private static List<DisplayCartLineDto> ParseCart(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<List<DisplayCartLineDto>>(json, JsonOpts) ?? new(); }
            catch { return new(); }
        }

        private static DisplayStateDto ToDto(DisplayState d) => new(
            d.Id, d.RegisterName, d.Token, d.TransactionId, d.Status, d.TotalUsd, d.RateLbp, d.TotalLbp, d.WhishRequestId, d.UpdatedOn);
        private static BaseResponse<T> Fail<T>(string error, string msg) => new(false, error, msg);
        private static BaseResponse<T> Ok<T>(T data, string msg) => new(true, null, msg, data);
    }
}
