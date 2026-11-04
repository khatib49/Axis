using System.Security.Cryptography;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    public class GiftCardService : IGiftCardService
    {
        // No 0/O/1/I so a code read over the phone can't be mis-typed.
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 16;

        private readonly ApplicationDbContext _db;
        private readonly IPromotionInvoiceAdjuster _adjuster;
        private readonly IPromotionAccountingService _accounting;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<GiftCardService> _logger;

        public GiftCardService(ApplicationDbContext db, IPromotionInvoiceAdjuster adjuster,
            IPromotionAccountingService accounting, ICurrentUserAccessor user, ILogger<GiftCardService> logger)
        {
            _db = db;
            _adjuster = adjuster;
            _accounting = accounting;
            _user = user;
            _logger = logger;
        }

        // ── helpers ──────────────────────────────────────────────────────
        public static string GenerateCode()
        {
            var chars = new char[CodeLength];
            var bytes = RandomNumberGenerator.GetBytes(CodeLength);
            for (var i = 0; i < CodeLength; i++) chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
            return new string(chars);
        }

        public static string NormaliseCode(string? code)
            => (code ?? "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

        /// <summary>Status as it should read right now (an Active card past its expiry is Expired).</summary>
        public static string EffectiveStatus(GiftCard c, DateTime nowUtc)
        {
            if (c.Status == GiftCardStatus.Disabled) return GiftCardStatus.Disabled;
            if (c.ExpiresOn.HasValue && c.ExpiresOn.Value <= nowUtc) return GiftCardStatus.Expired;
            if (c.Balance <= 0) return GiftCardStatus.Depleted;
            return GiftCardStatus.Active;
        }

        /// <summary>Pure redeem rule: can <paramref name="amount"/> be taken from the card now?</summary>
        public static (bool Ok, string? Reason) CanRedeem(GiftCard c, decimal amount, DateTime nowUtc)
        {
            var status = EffectiveStatus(c, nowUtc);
            if (status == GiftCardStatus.Disabled) return (false, "This gift card has been disabled.");
            if (status == GiftCardStatus.Expired) return (false, "This gift card has expired.");
            if (status == GiftCardStatus.Depleted) return (false, "This gift card has no remaining balance.");
            if (amount <= 0) return (false, "Amount must be greater than zero.");
            if (amount > c.Balance) return (false, $"Insufficient balance: the card holds {c.Balance:F2}.");
            return (true, null);
        }

        private static GiftCardDto ToDto(GiftCard c) => new(
            c.Id, c.Code, c.InitialAmount, c.Balance, c.CurrencyCode, c.IssuedOn, c.ExpiresOn,
            c.IssuedTo, c.UserId, c.Status, c.Notes, c.CreatedBy, c.BranchId);

        private static GiftCardTransactionDto ToDto(GiftCardTransaction t) => new(
            t.Id, t.GiftCardId, t.Type, t.Amount, t.TransactionId, t.BalanceAfter, t.On, t.By, t.Note);

        private GiftCardTransaction AddLedger(GiftCard card, string type, decimal amount, int? transactionId, string? note)
        {
            var row = new GiftCardTransaction
            {
                GiftCard = card,
                Type = type,
                Amount = Math.Abs(amount),
                TransactionId = transactionId,
                BalanceAfter = card.Balance,
                On = DateTime.UtcNow,
                By = _user.UserName,
                Note = note,
            };
            card.Transactions.Add(row);
            return row;
        }

        /// <summary>
        /// Phase 3: ledger posting AFTER the card movement is saved. A failure
        /// (locked period, missing account) is logged and appended to the
        /// message; the card movement is never rolled back — the posting stays
        /// pending for POST api/promotions/accounting/repost.
        /// </summary>
        private async Task<string> PostAsync(Func<Task<BaseResponse<PromotionPostingResultDto>>> post, string message)
        {
            try
            {
                var r = await post();
                if (r.Success) return message;
                _logger.LogWarning("Gift card accounting posting deferred: {Error}", r.Error);
                return $"{message} (accounting pending: {r.Error})";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gift card accounting posting failed");
                return $"{message} (accounting pending: {ex.Message})";
            }
        }

        private void RefreshStatus(GiftCard card)
        {
            if (card.Status == GiftCardStatus.Disabled) return;
            card.Status = EffectiveStatus(card, DateTime.UtcNow);
        }

        // ── admin ────────────────────────────────────────────────────────
        public async Task<BaseResponse<GiftCardDto>> IssueAsync(GiftCardIssueDto dto, CancellationToken ct = default)
        {
            if (dto.Amount <= 0)
                return new BaseResponse<GiftCardDto>(false, "Invalid amount", "Amount must be greater than zero.");
            if (dto.ExpiresOn.HasValue && dto.ExpiresOn.Value <= DateTime.UtcNow)
                return new BaseResponse<GiftCardDto>(false, "Invalid expiry", "Expiry must be in the future.");

            string code;
            var attempts = 0;
            do
            {
                code = GenerateCode();
                if (++attempts > 10)
                    return new BaseResponse<GiftCardDto>(false, "Code generation failed", "Could not generate a unique code; try again.");
            } while (await _db.Set<GiftCard>().AnyAsync(g => g.Code == code, ct));

            var card = new GiftCard
            {
                Code = code,
                InitialAmount = Math.Round(dto.Amount, 2),
                Balance = Math.Round(dto.Amount, 2),
                CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? "USD" : dto.CurrencyCode.Trim().ToUpperInvariant(),
                IssuedOn = DateTime.UtcNow,
                ExpiresOn = dto.ExpiresOn,
                IssuedTo = dto.IssuedTo?.Trim(),
                UserId = dto.UserId,
                Status = GiftCardStatus.Active,
                Notes = dto.Notes,
                CreatedBy = _user.UserName,
                BranchId = _user.BranchId,
            };
            var load = AddLedger(card, GiftCardTransactionType.Load, card.Balance, null, "Issued");

            _db.Add(card);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Gift card {Code} issued for {Amount} by {User}", card.Code, card.Balance, _user.UserName);
            var msg = await PostAsync(() => _accounting.OnGiftCardIssuedOrLoadedAsync(card.Id, load.Id, load.Amount, ct), "Gift card issued");
            return new BaseResponse<GiftCardDto>(true, null, msg, ToDto(card));
        }

        public async Task<PaginatedResponse<GiftCardDto>> ListAsync(GiftCardFilterDto filter, CancellationToken ct = default)
        {
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);

            var q = _db.Set<GiftCard>().AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(g => g.Status == filter.Status);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim().ToUpperInvariant();
                q = q.Where(g => g.Code.Contains(s)
                              || (g.IssuedTo != null && g.IssuedTo.ToUpper().Contains(s))
                              || (g.Notes != null && g.Notes.ToUpper().Contains(s)));
            }

            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(g => g.IssuedOn)
                .Skip((page - 1) * size).Take(size)
                .ToListAsync(ct);

            return new PaginatedResponse<GiftCardDto>(total, rows.Select(ToDto).ToList(), page, size);
        }

        public async Task<GiftCardDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<GiftCard>().AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
            return c is null ? null : ToDto(c);
        }

        public async Task<List<GiftCardTransactionDto>> GetTransactionsAsync(int id, CancellationToken ct = default)
            => await _db.Set<GiftCardTransaction>().AsNoTracking()
                .Where(t => t.GiftCardId == id)
                .OrderByDescending(t => t.On).ThenByDescending(t => t.Id)
                .Select(t => new GiftCardTransactionDto(t.Id, t.GiftCardId, t.Type, t.Amount, t.TransactionId, t.BalanceAfter, t.On, t.By, t.Note))
                .ToListAsync(ct);

        public async Task<BaseResponse<GiftCardDto>> DisableAsync(int id, string? note, CancellationToken ct = default)
        {
            var card = await _db.Set<GiftCard>().FirstOrDefaultAsync(g => g.Id == id, ct);
            if (card is null) return new BaseResponse<GiftCardDto>(false, "Not found", "Gift card not found.");
            card.Status = GiftCardStatus.Disabled;
            if (!string.IsNullOrWhiteSpace(note)) card.Notes = string.IsNullOrWhiteSpace(card.Notes) ? note : $"{card.Notes}\n{note}";
            AddLedger(card, GiftCardTransactionType.Adjust, 0, null, $"Disabled. {note}".Trim());
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<GiftCardDto>(true, null, "Gift card disabled", ToDto(card));
        }

        public async Task<BaseResponse<GiftCardDto>> AdjustAsync(int id, GiftCardAdjustDto dto, CancellationToken ct = default)
        {
            if (dto.Delta == 0)
                return new BaseResponse<GiftCardDto>(false, "Invalid amount", "Delta cannot be zero.");
            var card = await _db.Set<GiftCard>().FirstOrDefaultAsync(g => g.Id == id, ct);
            if (card is null) return new BaseResponse<GiftCardDto>(false, "Not found", "Gift card not found.");
            if (card.Status == GiftCardStatus.Disabled)
                return new BaseResponse<GiftCardDto>(false, "Disabled", "A disabled card cannot be adjusted.");
            var newBalance = Math.Round(card.Balance + dto.Delta, 2);
            if (newBalance < 0)
                return new BaseResponse<GiftCardDto>(false, "Invalid amount", $"Balance cannot go below zero (current {card.Balance:F2}).");
            card.Balance = newBalance;
            RefreshStatus(card);
            AddLedger(card, GiftCardTransactionType.Adjust, dto.Delta, null, dto.Note ?? (dto.Delta > 0 ? "Manual increase" : "Manual decrease"));
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<GiftCardDto>(true, null, "Gift card adjusted", ToDto(card));
        }

        public async Task<BaseResponse<GiftCardDto>> LoadAsync(int id, GiftCardLoadDto dto, CancellationToken ct = default)
        {
            if (dto.Amount <= 0)
                return new BaseResponse<GiftCardDto>(false, "Invalid amount", "Amount must be greater than zero.");
            var card = await _db.Set<GiftCard>().FirstOrDefaultAsync(g => g.Id == id, ct);
            if (card is null) return new BaseResponse<GiftCardDto>(false, "Not found", "Gift card not found.");
            if (card.Status == GiftCardStatus.Disabled)
                return new BaseResponse<GiftCardDto>(false, "Disabled", "A disabled card cannot be loaded.");
            if (EffectiveStatus(card, DateTime.UtcNow) == GiftCardStatus.Expired)
                return new BaseResponse<GiftCardDto>(false, "Expired", "An expired card cannot be loaded; extend its expiry first.");
            card.Balance = Math.Round(card.Balance + dto.Amount, 2);
            card.InitialAmount = Math.Round(card.InitialAmount + dto.Amount, 2);
            RefreshStatus(card);
            var load = AddLedger(card, GiftCardTransactionType.Load, dto.Amount, null, dto.Note ?? "Loaded");
            await _db.SaveChangesAsync(ct);
            var msg = await PostAsync(() => _accounting.OnGiftCardIssuedOrLoadedAsync(card.Id, load.Id, load.Amount, ct), "Value loaded");
            return new BaseResponse<GiftCardDto>(true, null, msg, ToDto(card));
        }

        public async Task<BaseResponse<GiftCardDto>> RefundAsync(int id, GiftCardRefundDto dto, CancellationToken ct = default)
        {
            if (dto.Amount <= 0)
                return new BaseResponse<GiftCardDto>(false, "Invalid amount", "Amount must be greater than zero.");
            var card = await _db.Set<GiftCard>().Include(g => g.Transactions).FirstOrDefaultAsync(g => g.Id == id, ct);
            if (card is null) return new BaseResponse<GiftCardDto>(false, "Not found", "Gift card not found.");
            if (card.Status == GiftCardStatus.Disabled)
                return new BaseResponse<GiftCardDto>(false, "Disabled", "A disabled card cannot receive a refund.");

            if (dto.TransactionId.HasValue)
            {
                var redeemed = card.Transactions.Where(t => t.Type == GiftCardTransactionType.Redeem && t.TransactionId == dto.TransactionId).Sum(t => t.Amount);
                var refunded = card.Transactions.Where(t => t.Type == GiftCardTransactionType.Refund && t.TransactionId == dto.TransactionId).Sum(t => t.Amount);
                if (redeemed <= 0)
                    return new BaseResponse<GiftCardDto>(false, "Nothing to refund", "This card was not used on that invoice.");
                if (refunded + dto.Amount > redeemed)
                    return new BaseResponse<GiftCardDto>(false, "Too much", $"Only {redeemed - refunded:F2} of that invoice can still be refunded to this card.");
            }

            card.Balance = Math.Round(card.Balance + dto.Amount, 2);
            RefreshStatus(card);
            var refund = AddLedger(card, GiftCardTransactionType.Refund, dto.Amount, dto.TransactionId, dto.Note ?? "Refund to card");
            await _db.SaveChangesAsync(ct);
            var msg = await PostAsync(() => _accounting.OnGiftCardRefundedAsync(card.Id, refund.Id, refund.Amount, dto.TransactionId, ct), "Refunded to card");
            return new BaseResponse<GiftCardDto>(true, null, msg, ToDto(card));
        }

        // ── till ─────────────────────────────────────────────────────────
        public async Task<BaseResponse<GiftCardLookupDto>> LookupAsync(string code, CancellationToken ct = default)
        {
            var norm = NormaliseCode(code);
            if (norm.Length == 0) return new BaseResponse<GiftCardLookupDto>(false, "Invalid code", "Enter a gift card code.");
            var card = await _db.Set<GiftCard>().AsNoTracking().FirstOrDefaultAsync(g => g.Code == norm, ct);
            if (card is null) return new BaseResponse<GiftCardLookupDto>(false, "Not found", "No gift card with that code.");

            var status = EffectiveStatus(card, DateTime.UtcNow);
            var (ok, reason) = CanRedeem(card, card.Balance > 0 ? 0.01m : 0m, DateTime.UtcNow);
            return new BaseResponse<GiftCardLookupDto>(true, null, "OK",
                new GiftCardLookupDto(card.Code, card.Balance, card.CurrencyCode, status, ok, reason, card.ExpiresOn, card.IssuedTo));
        }

        public async Task<BaseResponse<GiftCardRedeemResultDto>> RedeemAsync(GiftCardRedeemDto dto, CancellationToken ct = default)
        {
            var norm = NormaliseCode(dto.Code);
            var amount = Math.Round(dto.Amount, 2);
            if (norm.Length == 0) return new BaseResponse<GiftCardRedeemResultDto>(false, "Invalid code", "Enter a gift card code.");

            var card = await _db.Set<GiftCard>().Include(g => g.Transactions).FirstOrDefaultAsync(g => g.Code == norm, ct);
            if (card is null) return new BaseResponse<GiftCardRedeemResultDto>(false, "Not found", "No gift card with that code.");

            var (ok, reason) = CanRedeem(card, amount, DateTime.UtcNow);
            if (!ok) return new BaseResponse<GiftCardRedeemResultDto>(false, "Cannot redeem", reason!);

            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (trx is null) return new BaseResponse<GiftCardRedeemResultDto>(false, "Invalid invoice", "The specified invoice does not exist.");
            if (trx.StatusId != PromotionInvoiceAdjuster.OpenInvoiceStatusId)
                return new BaseResponse<GiftCardRedeemResultDto>(false, "Invoice not open", "A gift card can only be applied to an open invoice.");
            if (amount > trx.TotalPrice)
                return new BaseResponse<GiftCardRedeemResultDto>(false, "Amount too high",
                    $"The invoice only has {trx.TotalPrice:F2} left to pay; apply at most that.");

            // One redeem per card per invoice per balance snapshot — a repeated
            // click with the same card on the same invoice is refused by the
            // adjuster key unless the cashier really applies a second amount.
            var seq = card.Transactions.Count(t => t.Type == GiftCardTransactionType.Redeem && t.TransactionId == dto.TransactionId) + 1;
            var key = $"giftcard:{card.Code}:tx{dto.TransactionId}#{seq}";

            var adj = await _adjuster.ApplyAsync(dto.TransactionId, -amount, key,
                $"Gift card {card.Code} applied as payment: {amount:F2}", ct);
            if (!adj.Success)
                return new BaseResponse<GiftCardRedeemResultDto>(false, adj.AlreadyApplied ? "Already applied" : "Cannot apply", adj.Error ?? "Could not apply the gift card.");

            card.Balance = Math.Round(card.Balance - amount, 2);
            RefreshStatus(card);
            var redeem = AddLedger(card, GiftCardTransactionType.Redeem, amount, dto.TransactionId, $"Applied as payment on invoice #{dto.TransactionId}");

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Gift card {Code} redeemed {Amount} on invoice {TxId}", card.Code, amount, dto.TransactionId);
            var msg = await PostAsync(() => _accounting.OnGiftCardRedeemedAsync(card.Id, redeem.Id, redeem.Amount, dto.TransactionId, ct), "Gift card applied as payment");

            return new BaseResponse<GiftCardRedeemResultDto>(true, null, msg,
                new GiftCardRedeemResultDto(card.Code, amount, card.Balance, dto.TransactionId, adj.OldTotal, adj.NewTotal, card.Status));
        }
    }
}
