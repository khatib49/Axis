using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    public class CouponService : ICouponService
    {
        private readonly ApplicationDbContext _db;
        private readonly IPromotionInvoiceAdjuster _adjuster;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<CouponService> _logger;

        public CouponService(ApplicationDbContext db, IPromotionInvoiceAdjuster adjuster,
            ICurrentUserAccessor user, ILogger<CouponService> logger)
        {
            _db = db;
            _adjuster = adjuster;
            _user = user;
            _logger = logger;
        }

        public static string NormaliseCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

        private static CouponDto ToDto(Coupon c) => new(
            c.Id, c.Code, c.Name, c.Type, c.Value, c.MinOrderAmount, c.MaxDiscountAmount, c.MaxUses,
            c.MaxUsesPerCustomer, c.UsedCount, c.ValidFrom, c.ValidTo, c.CategoryIdsCsv, c.IsActive,
            c.CreatedBy, c.CreatedOn, c.BranchId);

        private static string? Validate(CouponUpsertDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code)) return "Code is required.";
            if (NormaliseCode(dto.Code).Length > 40) return "Code must be 40 characters or fewer.";
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
            var isPercent = string.Equals(dto.Type, CouponType.Percent, StringComparison.OrdinalIgnoreCase);
            var isFixed = string.Equals(dto.Type, CouponType.Fixed, StringComparison.OrdinalIgnoreCase);
            if (!isPercent && !isFixed) return "Type must be Percent or Fixed.";
            if (dto.Value <= 0) return "Value must be greater than zero.";
            if (isPercent && dto.Value > 100) return "A percent coupon cannot exceed 100.";
            if (dto.MinOrderAmount is < 0) return "Minimum order cannot be negative.";
            if (dto.MaxDiscountAmount is <= 0) return "Maximum discount must be greater than zero when set.";
            if (dto.MaxUses is <= 0) return "Max uses must be greater than zero when set.";
            if (dto.MaxUsesPerCustomer is <= 0) return "Max uses per customer must be greater than zero when set.";
            if (dto.ValidFrom.HasValue && dto.ValidTo.HasValue && dto.ValidTo < dto.ValidFrom) return "Valid-to must be after valid-from.";
            return null;
        }

        private static void Apply(Coupon c, CouponUpsertDto dto)
        {
            c.Code = NormaliseCode(dto.Code);
            c.Name = dto.Name.Trim();
            c.Type = string.Equals(dto.Type, CouponType.Fixed, StringComparison.OrdinalIgnoreCase) ? CouponType.Fixed : CouponType.Percent;
            c.Value = Math.Round(dto.Value, 2);
            c.MinOrderAmount = dto.MinOrderAmount;
            c.MaxDiscountAmount = dto.MaxDiscountAmount;
            c.MaxUses = dto.MaxUses;
            c.MaxUsesPerCustomer = dto.MaxUsesPerCustomer;
            c.ValidFrom = dto.ValidFrom;
            c.ValidTo = dto.ValidTo;
            c.CategoryIdsCsv = PromoCsv.Normalise(dto.CategoryIdsCsv);
            c.IsActive = dto.IsActive;
        }

        // ── admin CRUD ───────────────────────────────────────────────────
        public async Task<PaginatedResponse<CouponDto>> ListAsync(CouponFilterDto filter, CancellationToken ct = default)
        {
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);
            var q = _db.Set<Coupon>().AsNoTracking().AsQueryable();
            if (filter.IsActive.HasValue) q = q.Where(c => c.IsActive == filter.IsActive.Value);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim().ToUpperInvariant();
                q = q.Where(c => c.Code.Contains(s) || c.Name.ToUpper().Contains(s));
            }
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(c => c.CreatedOn).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PaginatedResponse<CouponDto>(total, rows.Select(ToDto).ToList(), page, size);
        }

        public async Task<CouponDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<Coupon>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null ? null : ToDto(c);
        }

        public async Task<BaseResponse<CouponDto>> CreateAsync(CouponUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err != null) return new BaseResponse<CouponDto>(false, "Validation", err);
            var code = NormaliseCode(dto.Code);
            if (await _db.Set<Coupon>().AnyAsync(c => c.Code == code, ct))
                return new BaseResponse<CouponDto>(false, "Duplicate", $"A coupon with code {code} already exists.");

            var c = new Coupon { CreatedBy = _user.UserName, CreatedOn = DateTime.UtcNow, BranchId = _user.BranchId };
            Apply(c, dto);
            _db.Add(c);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<CouponDto>(true, null, "Coupon created", ToDto(c));
        }

        public async Task<BaseResponse<CouponDto>> UpdateAsync(int id, CouponUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err != null) return new BaseResponse<CouponDto>(false, "Validation", err);
            var c = await _db.Set<Coupon>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return new BaseResponse<CouponDto>(false, "Not found", "Coupon not found.");
            var code = NormaliseCode(dto.Code);
            if (await _db.Set<Coupon>().AnyAsync(x => x.Code == code && x.Id != id, ct))
                return new BaseResponse<CouponDto>(false, "Duplicate", $"A coupon with code {code} already exists.");
            Apply(c, dto);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<CouponDto>(true, null, "Coupon updated", ToDto(c));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<Coupon>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return false;
            // Keep history: a coupon that was used is deactivated, never deleted.
            if (await _db.Set<CouponRedemption>().AnyAsync(r => r.CouponId == id, ct))
                c.IsActive = false;
            else
                _db.Remove(c);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<List<CouponRedemptionDto>> GetRedemptionsAsync(int couponId, CancellationToken ct = default)
            => await _db.Set<CouponRedemption>().AsNoTracking()
                .Where(r => r.CouponId == couponId)
                .OrderByDescending(r => r.RedeemedOn)
                .Select(r => new CouponRedemptionDto(r.Id, r.CouponId, r.Coupon.Code, r.TransactionId, r.UserId, r.DiscountAmount, r.RedeemedOn, r.RedeemedBy))
                .ToListAsync(ct);

        // ── evaluation ───────────────────────────────────────────────────
        private async Task<List<PromoLine>> LoadLinesAsync(int transactionId, CancellationToken ct)
            => await _db.Set<TransactionItem>().AsNoTracking()
                .Where(ti => ti.TransactionRecordId == transactionId && !ti.IsIncluded)
                .Select(ti => new PromoLine(ti.ItemId, ti.Item.CategoryId, ti.Item.Price, ti.Quantity))
                .ToListAsync(ct);

        private async Task<int> CustomerUsesAsync(int couponId, int? userId, CancellationToken ct)
            => userId.HasValue
                ? await _db.Set<CouponRedemption>().CountAsync(r => r.CouponId == couponId && r.UserId == userId, ct)
                : 0;

        public async Task<BaseResponse<CouponValidationResultDto>> ValidateAsync(CouponValidateDto dto, CancellationToken ct = default)
        {
            var code = NormaliseCode(dto.Code);
            if (code.Length == 0) return new BaseResponse<CouponValidationResultDto>(false, "Invalid code", "Enter a coupon code.");
            var c = await _db.Set<Coupon>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (c is null) return new BaseResponse<CouponValidationResultDto>(false, "Not found", "No coupon with that code.");

            decimal orderAmount;
            IReadOnlyDictionary<int, decimal>? categoryAmounts = null;
            var userId = dto.UserId;

            if (dto.TransactionId.HasValue)
            {
                var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId.Value, ct);
                if (trx is null) return new BaseResponse<CouponValidationResultDto>(false, "Invalid invoice", "The specified invoice does not exist.");
                if (trx.StatusId != PromotionInvoiceAdjuster.OpenInvoiceStatusId)
                    return new BaseResponse<CouponValidationResultDto>(false, "Invoice not open", "Coupons can only be applied to an open invoice.");
                if (await _db.Set<CouponRedemption>().AnyAsync(r => r.CouponId == c.Id && r.TransactionId == trx.Id, ct))
                    return Result(c, false, "This coupon was already applied to that invoice.", trx.TotalPrice, 0, 0);
                orderAmount = trx.TotalPrice;
                categoryAmounts = CouponEngine.CategoryAmounts(await LoadLinesAsync(trx.Id, ct));
                userId ??= trx.UserId;
            }
            else
            {
                orderAmount = dto.OrderAmount ?? 0m;
                if (dto.CategoryIds is { Count: > 0 })
                {
                    // What-if from the till: no per-category amounts known, so a
                    // category-restricted coupon is evaluated over the whole order
                    // when at least one listed category is on it.
                    var cats = PromoCsv.ParseInts(c.CategoryIdsCsv);
                    if (cats.Count > 0 && !dto.CategoryIds.Any(cats.Contains))
                        return Result(c, false, "None of the order's categories are covered by this coupon.", orderAmount, 0, 0);
                }
            }

            var uses = await CustomerUsesAsync(c.Id, userId, ct);
            var ev = CouponEngine.Evaluate(c, orderAmount, categoryAmounts, uses, DateTime.UtcNow);
            return Result(c, ev.Eligible, ev.Reason, ev.OrderAmount, ev.EligibleAmount, ev.DiscountAmount);
        }

        private static BaseResponse<CouponValidationResultDto> Result(Coupon c, bool eligible, string? reason, decimal order, decimal eligibleAmt, decimal discount)
            => new(true, null, eligible ? "Eligible" : reason ?? "Not eligible",
                new CouponValidationResultDto(eligible, reason, c.Code, c.Name, c.Type, c.Value, order, eligibleAmt, discount));

        public async Task<BaseResponse<CouponRedeemResultDto>> RedeemAsync(CouponRedeemDto dto, CancellationToken ct = default)
        {
            var code = NormaliseCode(dto.Code);
            if (code.Length == 0) return new BaseResponse<CouponRedeemResultDto>(false, "Invalid code", "Enter a coupon code.");
            var c = await _db.Set<Coupon>().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (c is null) return new BaseResponse<CouponRedeemResultDto>(false, "Not found", "No coupon with that code.");

            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (trx is null) return new BaseResponse<CouponRedeemResultDto>(false, "Invalid invoice", "The specified invoice does not exist.");
            if (trx.StatusId != PromotionInvoiceAdjuster.OpenInvoiceStatusId)
                return new BaseResponse<CouponRedeemResultDto>(false, "Invoice not open", "Coupons can only be applied to an open invoice.");
            if (await _db.Set<CouponRedemption>().AnyAsync(r => r.CouponId == c.Id && r.TransactionId == trx.Id, ct))
                return new BaseResponse<CouponRedeemResultDto>(false, "Already applied", "This coupon was already applied to that invoice.");

            var lines = await LoadLinesAsync(trx.Id, ct);
            var uses = await CustomerUsesAsync(c.Id, trx.UserId, ct);
            var ev = CouponEngine.Evaluate(c, trx.TotalPrice, CouponEngine.CategoryAmounts(lines), uses, DateTime.UtcNow);
            if (!ev.Eligible)
                return new BaseResponse<CouponRedeemResultDto>(false, "Not eligible", ev.Reason ?? "Coupon cannot be applied.");

            var adj = await _adjuster.ApplyAsync(trx.Id, -ev.DiscountAmount, $"coupon:{c.Code}",
                $"Coupon {c.Code} ({c.Name}) discount {ev.DiscountAmount:F2}", ct);
            if (!adj.Success)
                return new BaseResponse<CouponRedeemResultDto>(false, adj.AlreadyApplied ? "Already applied" : "Cannot apply", adj.Error ?? "Could not apply the coupon.");

            var redemption = new CouponRedemption
            {
                CouponId = c.Id,
                TransactionId = trx.Id,
                UserId = trx.UserId,
                DiscountAmount = ev.DiscountAmount,
                RedeemedOn = DateTime.UtcNow,
                RedeemedBy = _user.UserName,
            };
            c.UsedCount += 1;
            _db.Add(redemption);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Coupon {Code} redeemed on invoice {TxId} for {Amount}", c.Code, trx.Id, ev.DiscountAmount);
            return new BaseResponse<CouponRedeemResultDto>(true, null, "Coupon applied",
                new CouponRedeemResultDto(c.Code, trx.Id, ev.DiscountAmount, adj.OldTotal, adj.NewTotal, redemption.Id));
        }
    }
}
