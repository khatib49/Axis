using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    /// <summary>
    /// Phase 3: books the ledger side of gift cards and promotion discounts.
    ///
    /// Accounts (created once, by number, if missing):
    ///   2150 Gift Card Liability   (Liability, no manual entry, system)
    ///   4950 Promotional Discounts (Revenue type, contra-revenue)
    /// and the existing 1000 Cash on Hand / 4100 FNB sales revenue that the
    /// transaction journal (JournalService.CreateJournalEntryFromTransactionAsync)
    /// uses as its general sales account.
    ///
    /// Entries:
    ///   gift card issued / loaded : DR 1000 / CR 2150   (GiftCardLoad)
    ///   gift card redeemed        : DR 2150 / CR 4100   (GiftCardRedeem)
    ///   gift card refunded        : DR 4100 / CR 2150   (GiftCardRefund — value put back on the
    ///                               card after a voided line: the reverse of the redeem, the venue
    ///                               owes the holder again; no cash moves)
    ///   discount applied  (delta<0): DR 4950 / CR 4100  (Promotion{Coupon|HappyHour|Combo})
    ///   discount refunded (delta>0): DR 4100 / CR 4950
    ///
    /// Idempotency: one Promo_AccountingPostings row per (ReferenceType,
    /// ReferenceId, Sequence). A repeat returns the existing row (AlreadyPosted)
    /// and never books a second entry. A locked period / missing account /
    /// journal failure leaves the row Pending with its Error; the caller logs
    /// it and keeps the business action. RepostPendingAsync retries later.
    /// </summary>
    public class PromotionAccountingService : IPromotionAccountingService
    {
        public const string CashAccountNumber = "1000";
        public const string LiabilityAccountNumber = "2150";
        public const string LiabilityAccountName = "Gift Card Liability";
        public const string DiscountAccountNumber = "4950";
        public const string DiscountAccountName = "Promotional Discounts";
        /// <summary>The general sales account the transaction journal credits for unmapped F&B categories.</summary>
        public const string SalesAccountNumber = "4100";

        private readonly ApplicationDbContext _db;
        private readonly IJournalService _journal;
        private readonly IAccountService _accounts;
        private readonly IAccountingPeriodService _periods;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<PromotionAccountingService> _logger;

        public PromotionAccountingService(ApplicationDbContext db, IJournalService journal, IAccountService accounts,
            IAccountingPeriodService periods, ICurrentUserAccessor user, ILogger<PromotionAccountingService> logger)
        {
            _db = db;
            _journal = journal;
            _accounts = accounts;
            _periods = periods;
            _user = user;
            _logger = logger;
        }

        // ── accounts ─────────────────────────────────────────────────────
        public async Task<BaseResponse<PromotionAccountsDto>> EnsureAccountsAsync(CancellationToken ct = default)
        {
            try
            {
                var createdLiability = false;
                var createdDiscount = false;

                var liability = await FindAccountAsync(LiabilityAccountNumber, ct);
                if (liability is null)
                {
                    var type = await _db.Set<AccountType>().AsNoTracking().FirstOrDefaultAsync(t => t.TypeName == "Liability", ct);
                    if (type is null) return new BaseResponse<PromotionAccountsDto>(false, "Account type 'Liability' not found", "Chart of accounts is not seeded.");
                    var r = await _accounts.CreateAccountAsync(new AccountCreateDto(LiabilityAccountNumber, LiabilityAccountName, type.Id, null,
                        "Outstanding value on issued gift cards (owed to card holders). Booked by the Promotions module; no manual entry.", AllowManualEntry: false), _user.UserId, ct);
                    if (!r.Success) return new BaseResponse<PromotionAccountsDto>(false, r.Error ?? "Could not create 2150", r.Message ?? "");
                    liability = await _db.Set<Account>().FirstAsync(a => a.Id == r.Data!.Id, ct);
                    liability.IsSystemAccount = true;
                    await _db.SaveChangesAsync(ct);
                    createdLiability = true;
                    _logger.LogInformation("Created account {No} {Name}", LiabilityAccountNumber, LiabilityAccountName);
                }

                var discount = await FindAccountAsync(DiscountAccountNumber, ct);
                if (discount is null)
                {
                    var type = await _db.Set<AccountType>().AsNoTracking().FirstOrDefaultAsync(t => t.TypeName == "Revenue", ct);
                    if (type is null) return new BaseResponse<PromotionAccountsDto>(false, "Account type 'Revenue' not found", "Chart of accounts is not seeded.");
                    var r = await _accounts.CreateAccountAsync(new AccountCreateDto(DiscountAccountNumber, DiscountAccountName, type.Id, null,
                        "Contra-revenue: coupons, happy hour and combo discounts given (debit balance reduces gross sales). Booked by the Promotions module.", AllowManualEntry: false), _user.UserId, ct);
                    if (!r.Success) return new BaseResponse<PromotionAccountsDto>(false, r.Error ?? "Could not create 4950", r.Message ?? "");
                    discount = await _db.Set<Account>().FirstAsync(a => a.Id == r.Data!.Id, ct);
                    discount.IsSystemAccount = true;
                    await _db.SaveChangesAsync(ct);
                    createdDiscount = true;
                    _logger.LogInformation("Created account {No} {Name}", DiscountAccountNumber, DiscountAccountName);
                }

                var cash = await FindAccountAsync(CashAccountNumber, ct);
                if (cash is null) return new BaseResponse<PromotionAccountsDto>(false, $"Cash account ({CashAccountNumber}) not found", "");
                var sales = await FindAccountAsync(SalesAccountNumber, ct);
                if (sales is null) return new BaseResponse<PromotionAccountsDto>(false, $"Sales revenue account ({SalesAccountNumber}) not found", "");

                return new BaseResponse<PromotionAccountsDto>(true, null, "OK", new PromotionAccountsDto(
                    liability.Id, liability.AccountNumber, liability.AccountName,
                    discount.Id, discount.AccountNumber, discount.AccountName,
                    cash.Id, cash.AccountNumber, sales.Id, sales.AccountNumber, createdLiability, createdDiscount));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnsureAccounts failed");
                return new BaseResponse<PromotionAccountsDto>(false, "Could not prepare promotion accounts: " + ex.Message, "");
            }
        }

        private Task<Account?> FindAccountAsync(string number, CancellationToken ct)
            => _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == number && a.IsActive, ct);

        // ── posting hooks ────────────────────────────────────────────────
        public Task<BaseResponse<PromotionPostingResultDto>> OnGiftCardIssuedOrLoadedAsync(int giftCardId, int giftCardTransactionId, decimal amount, CancellationToken ct = default)
            => PostAsync(PromotionPostingType.GiftCardLoad, giftCardTransactionId, 1, Math.Abs(amount), null, giftCardId,
                $"Gift card #{giftCardId} loaded {Math.Abs(amount):F2} (card transaction #{giftCardTransactionId})", ct);

        public Task<BaseResponse<PromotionPostingResultDto>> OnGiftCardRedeemedAsync(int giftCardId, int giftCardTransactionId, decimal amount, int invoiceId, CancellationToken ct = default)
            => PostAsync(PromotionPostingType.GiftCardRedeem, giftCardTransactionId, 1, Math.Abs(amount), invoiceId, giftCardId,
                $"Gift card #{giftCardId} redeemed {Math.Abs(amount):F2} on invoice #{invoiceId} (card transaction #{giftCardTransactionId})", ct);

        public Task<BaseResponse<PromotionPostingResultDto>> OnGiftCardRefundedAsync(int giftCardId, int giftCardTransactionId, decimal amount, int? invoiceId, CancellationToken ct = default)
            => PostAsync(PromotionPostingType.GiftCardRefund, giftCardTransactionId, 1, Math.Abs(amount), invoiceId, giftCardId,
                $"Gift card #{giftCardId} refunded {Math.Abs(amount):F2}" + (invoiceId.HasValue ? $" from invoice #{invoiceId}" : "") + $" (card transaction #{giftCardTransactionId})", ct);

        public Task<BaseResponse<PromotionPostingResultDto>> OnPromotionDiscountAsync(string kind, int referenceId, int sequence, int invoiceId, decimal delta, CancellationToken ct = default)
        {
            kind = (kind ?? "").Trim();
            if (kind != "Coupon" && kind != "HappyHour" && kind != "Combo")
                return Task.FromResult(new BaseResponse<PromotionPostingResultDto>(false, "Invalid promotion kind", $"Unknown promotion kind '{kind}'."));
            delta = Math.Round(delta, 2);
            if (delta == 0m)
                return Task.FromResult(new BaseResponse<PromotionPostingResultDto>(false, "Nothing to post", "Delta is zero."));
            var what = delta < 0 ? $"discount {-delta:F2} applied" : $"discount {delta:F2} refunded";
            return PostAsync(PromotionPostingType.ForPromotionKind(kind), referenceId, Math.Max(1, sequence), delta, invoiceId, null,
                $"{kind} {what} on invoice #{invoiceId} (application #{referenceId} v{Math.Max(1, sequence)})", ct);
        }

        // ── core ─────────────────────────────────────────────────────────
        private async Task<BaseResponse<PromotionPostingResultDto>> PostAsync(string type, int referenceId, int sequence, decimal amount,
            int? invoiceId, int? giftCardId, string description, CancellationToken ct, DateTime? on = null)
        {
            if (amount == 0m)
                return new BaseResponse<PromotionPostingResultDto>(false, "Nothing to post", "Amount is zero.");

            PromotionAccountingPosting? row = null;
            try
            {
                row = await _db.Set<PromotionAccountingPosting>()
                    .FirstOrDefaultAsync(p => p.ReferenceType == type && p.ReferenceId == referenceId && p.Sequence == sequence, ct);

                if (row is not null && row.Status == PromotionPostingStatus.Posted)
                {
                    _logger.LogDebug("Posting {Type}/{Ref}#{Seq} already booked as JE {Je}", type, referenceId, sequence, row.JournalEntryId);
                    return new BaseResponse<PromotionPostingResultDto>(true, null, "Already posted", await ToResultAsync(row, true, ct));
                }

                if (row is null)
                {
                    row = new PromotionAccountingPosting
                    {
                        ReferenceType = type,
                        ReferenceId = referenceId,
                        Sequence = sequence,
                        Amount = amount,
                        InvoiceId = invoiceId,
                        GiftCardId = giftCardId,
                        Description = Truncate(description, 500),
                        On = on ?? DateTime.UtcNow,
                        Status = PromotionPostingStatus.Pending,
                        By = _user.UserName ?? "system",
                        BranchId = _user.BranchId,
                    };
                    _db.Add(row);
                    await _db.SaveChangesAsync(ct);
                }

                return await AttemptAsync(row, ct);
            }
            catch (Exception ex)
            {
                // Never let the ledger break the till: the business action stands.
                _logger.LogError(ex, "Promotion posting {Type}/{Ref}#{Seq} failed", type, referenceId, sequence);
                if (row is not null)
                {
                    try { row.Error = Truncate(ex.Message, 500); await _db.SaveChangesAsync(ct); } catch { /* best effort */ }
                }
                return new BaseResponse<PromotionPostingResultDto>(false, "Accounting posting failed: " + ex.Message, "",
                    row is null ? null : new PromotionPostingResultDto(row.Id, type, referenceId, sequence, amount, PromotionPostingStatus.Pending, null, null, ex.Message));
            }
        }

        /// <summary>One attempt on an existing (tracked) Pending row: period check, accounts, create + post the entry.</summary>
        private async Task<BaseResponse<PromotionPostingResultDto>> AttemptAsync(PromotionAccountingPosting row, CancellationToken ct)
        {
            row.Attempts += 1;

            // Entry is dated when the business action happened (On == now on the first attempt).
            var period = await _periods.EnsureOpenAsync(row.On, ct);
            if (!period.Success)
                return await DeferAsync(row, period.Error ?? "Accounting period is locked", ct);

            var accounts = await EnsureAccountsAsync(ct);
            if (!accounts.Success || accounts.Data is null)
                return await DeferAsync(row, accounts.Error ?? "Promotion accounts unavailable", ct);

            var (debitId, creditId, debitNo, creditNo) = Resolve(row, accounts.Data);
            var abs = Math.Abs(row.Amount);

            if (row.JournalEntryId is null)
            {
                var dto = new JournalEntryCreateDto(row.On, row.Description ?? row.ReferenceType, row.ReferenceType, row.ReferenceId,
                    new List<JournalEntryLineCreateDto>
                    {
                        new(debitId, abs, 0, $"{debitNo} - {row.Description}"),
                        new(creditId, 0, abs, $"{creditNo} - {row.Description}"),
                    });
                var created = await _journal.CreateJournalEntryAsync(dto, _user.UserId, ct);
                if (!created.Success || created.Data is null)
                    return await DeferAsync(row, created.Error ?? "Journal refused the entry", ct);
                row.JournalEntryId = created.Data.Id;
            }

            var posted = await _journal.PostJournalEntryAsync(row.JournalEntryId.Value, _user.UserId, ct);
            if (!posted.Success)
            {
                // An already-posted entry (e.g. posted by hand from the journal page) counts as done.
                var je = await _db.Set<JournalEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == row.JournalEntryId, ct);
                if (je is null || !je.IsPosted || je.IsVoided)
                    return await DeferAsync(row, posted.Error ?? "Journal entry could not be posted", ct);
            }

            row.Status = PromotionPostingStatus.Posted;
            row.PostedOn = DateTime.UtcNow;
            row.Error = null;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Promotion posting {Type}/{Ref}#{Seq}: DR {Dr} / CR {Cr} {Amt} as JE {Je}",
                row.ReferenceType, row.ReferenceId, row.Sequence, debitNo, creditNo, abs, row.JournalEntryId);
            return new BaseResponse<PromotionPostingResultDto>(true, null, "Posted", await ToResultAsync(row, false, ct));
        }

        private async Task<BaseResponse<PromotionPostingResultDto>> DeferAsync(PromotionAccountingPosting row, string error, CancellationToken ct)
        {
            row.Status = PromotionPostingStatus.Pending;
            row.Error = Truncate(error, 500);
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Promotion posting {Type}/{Ref}#{Seq} left pending: {Error}", row.ReferenceType, row.ReferenceId, row.Sequence, error);
            return new BaseResponse<PromotionPostingResultDto>(false, error, "Accounting posting pending: " + error, await ToResultAsync(row, false, ct));
        }

        /// <summary>Debit / credit account for a posting, by its type and sign.</summary>
        private static (int DebitId, int CreditId, string DebitNo, string CreditNo) Resolve(PromotionAccountingPosting row, PromotionAccountsDto a)
        {
            switch (row.ReferenceType)
            {
                case PromotionPostingType.GiftCardLoad:
                    return (a.CashAccountId, a.LiabilityAccountId, a.CashAccountNumber, a.LiabilityAccountNumber);
                case PromotionPostingType.GiftCardRedeem:
                    return (a.LiabilityAccountId, a.SalesAccountId, a.LiabilityAccountNumber, a.SalesAccountNumber);
                case PromotionPostingType.GiftCardRefund:
                    return (a.SalesAccountId, a.LiabilityAccountId, a.SalesAccountNumber, a.LiabilityAccountNumber);
                default:
                    if (!PromotionPostingType.IsPromotion(row.ReferenceType))
                        throw new InvalidOperationException($"Unknown posting type {row.ReferenceType}");
                    return row.Amount < 0
                        ? (a.DiscountAccountId, a.SalesAccountId, a.DiscountAccountNumber, a.SalesAccountNumber)   // discount given
                        : (a.SalesAccountId, a.DiscountAccountId, a.SalesAccountNumber, a.DiscountAccountNumber);  // discount clawed back
            }
        }

        private async Task<PromotionPostingResultDto> ToResultAsync(PromotionAccountingPosting row, bool already, CancellationToken ct)
        {
            string? number = null;
            if (row.JournalEntryId.HasValue)
                number = await _db.Set<JournalEntry>().AsNoTracking().Where(e => e.Id == row.JournalEntryId).Select(e => e.EntryNumber).FirstOrDefaultAsync(ct);
            return new PromotionPostingResultDto(row.Id, row.ReferenceType, row.ReferenceId, row.Sequence, row.Amount, row.Status,
                row.JournalEntryId, number, row.Error, already);
        }

        private static string? Truncate(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

        // ── queries ──────────────────────────────────────────────────────
        public async Task<List<PromotionPostingDto>> GetPostingsAsync(DateTime? from, DateTime? to, string? status, CancellationToken ct = default)
        {
            var q = _db.Set<PromotionAccountingPosting>().AsNoTracking().AsQueryable();
            if (from.HasValue) { var f = AsUtc(from.Value); q = q.Where(p => p.On >= f); }
            if (to.HasValue) { var t = AsUtc(to.Value); q = q.Where(p => p.On <= t); }
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
            var rows = await q.OrderByDescending(p => p.On).ThenByDescending(p => p.Id).Take(1000).ToListAsync(ct);
            return await DecorateAsync(rows, ct);
        }

        public async Task<List<PromotionPostingDto>> GetPendingAsync(CancellationToken ct = default)
        {
            var rows = await _db.Set<PromotionAccountingPosting>().AsNoTracking()
                .Where(p => p.Status == PromotionPostingStatus.Pending)
                .OrderBy(p => p.On).ThenBy(p => p.Id).ToListAsync(ct);
            return await DecorateAsync(rows, ct);
        }

        private async Task<List<PromotionPostingDto>> DecorateAsync(List<PromotionAccountingPosting> rows, CancellationToken ct)
        {
            var jeIds = rows.Where(r => r.JournalEntryId.HasValue).Select(r => r.JournalEntryId!.Value).Distinct().ToList();
            var entries = jeIds.Count == 0
                ? new Dictionary<int, JournalEntry>()
                : await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account)
                    .Where(e => jeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

            return rows.Select(r =>
            {
                entries.TryGetValue(r.JournalEntryId ?? -1, out var je);
                var dr = je?.Lines.Where(l => l.DebitAmount > 0).Select(l => l.Account?.AccountNumber).FirstOrDefault();
                var cr = je?.Lines.Where(l => l.CreditAmount > 0).Select(l => l.Account?.AccountNumber).FirstOrDefault();
                return new PromotionPostingDto(r.Id, r.ReferenceType, PromotionPostingType.Kind(r.ReferenceType), r.ReferenceId, r.Sequence,
                    r.Amount, r.InvoiceId, r.GiftCardId, r.JournalEntryId, je?.EntryNumber, r.Status, r.Error, r.Description,
                    r.On, r.PostedOn, r.Attempts, r.By, dr, cr);
            }).ToList();
        }

        public async Task<BaseResponse<PromotionRepostResultDto>> RepostPendingAsync(CancellationToken ct = default)
        {
            var pending = await _db.Set<PromotionAccountingPosting>()
                .Where(p => p.Status == PromotionPostingStatus.Pending)
                .OrderBy(p => p.On).ThenBy(p => p.Id).ToListAsync(ct);

            var results = new List<PromotionPostingResultDto>();
            var posted = 0;
            foreach (var row in pending)
            {
                BaseResponse<PromotionPostingResultDto> r;
                try { r = await AttemptAsync(row, ct); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Repost of posting {Id} failed", row.Id);
                    r = new BaseResponse<PromotionPostingResultDto>(false, ex.Message, "",
                        new PromotionPostingResultDto(row.Id, row.ReferenceType, row.ReferenceId, row.Sequence, row.Amount, row.Status, row.JournalEntryId, null, ex.Message));
                }
                if (r.Success) posted++;
                if (r.Data != null) results.Add(r.Data);
            }
            var still = pending.Count - posted;
            return new BaseResponse<PromotionRepostResultDto>(true, null,
                pending.Count == 0 ? "Nothing pending" : $"{posted} of {pending.Count} pending posting(s) booked" + (still > 0 ? $", {still} still pending" : ""),
                new PromotionRepostResultDto(pending.Count, posted, still, results));
        }

        /// <summary>
        /// Books every gift card movement / coupon redemption / current happy hour
        /// and combo saving that has no posting row yet (data from before phase 3).
        /// Entries are dated when the business action happened; a locked month
        /// leaves them pending for a later repost.
        /// </summary>
        public async Task<BaseResponse<PromotionRepostResultDto>> BackfillAsync(CancellationToken ct = default)
        {
            var results = new List<PromotionPostingResultDto>();
            var attempted = 0; var posted = 0;

            async Task Run(Task<BaseResponse<PromotionPostingResultDto>> task)
            {
                attempted++;
                var r = await task;
                if (r.Success && !(r.Data?.AlreadyPosted ?? false)) posted++;
                if (r.Data != null) results.Add(r.Data);
            }

            var known = await _db.Set<PromotionAccountingPosting>().AsNoTracking()
                .Select(p => new { p.ReferenceType, p.ReferenceId }).ToListAsync(ct);
            var knownSet = known.Select(k => (k.ReferenceType, k.ReferenceId)).ToHashSet();

            var gcTx = await _db.Set<GiftCardTransaction>().AsNoTracking()
                .Where(t => t.Amount > 0 && (t.Type == GiftCardTransactionType.Load || t.Type == GiftCardTransactionType.Redeem || t.Type == GiftCardTransactionType.Refund))
                .OrderBy(t => t.Id).ToListAsync(ct);
            foreach (var t in gcTx)
            {
                var type = t.Type == GiftCardTransactionType.Load ? PromotionPostingType.GiftCardLoad
                    : t.Type == GiftCardTransactionType.Redeem ? PromotionPostingType.GiftCardRedeem : PromotionPostingType.GiftCardRefund;
                if (knownSet.Contains((type, t.Id))) continue;
                var desc = type == PromotionPostingType.GiftCardLoad ? $"Gift card #{t.GiftCardId} loaded {t.Amount:F2} (card transaction #{t.Id}, backfill)"
                    : type == PromotionPostingType.GiftCardRedeem ? $"Gift card #{t.GiftCardId} redeemed {t.Amount:F2} on invoice #{t.TransactionId} (card transaction #{t.Id}, backfill)"
                    : $"Gift card #{t.GiftCardId} refunded {t.Amount:F2} (card transaction #{t.Id}, backfill)";
                await Run(PostAsync(type, t.Id, 1, t.Amount, t.TransactionId, t.GiftCardId, desc, ct, t.On));
            }

            var coupons = await _db.Set<CouponRedemption>().AsNoTracking().Where(r => r.DiscountAmount > 0).OrderBy(r => r.Id).ToListAsync(ct);
            foreach (var r in coupons)
            {
                if (knownSet.Contains((PromotionPostingType.PromotionCoupon, r.Id))) continue;
                await Run(PostAsync(PromotionPostingType.PromotionCoupon, r.Id, 1, -r.DiscountAmount, r.TransactionId, null,
                    $"Coupon discount {r.DiscountAmount:F2} applied on invoice #{r.TransactionId} (application #{r.Id} v1, backfill)", ct, r.RedeemedOn));
            }

            var hh = await _db.Set<HappyHourApplication>().AsNoTracking().Where(a => a.Amount > 0).OrderBy(a => a.Id).ToListAsync(ct);
            foreach (var a in hh)
            {
                if (knownSet.Contains((PromotionPostingType.PromotionHappyHour, a.Id))) continue;
                await Run(PostAsync(PromotionPostingType.PromotionHappyHour, a.Id, Math.Max(1, a.Version), -a.Amount, a.TransactionId, null,
                    $"HappyHour discount {a.Amount:F2} applied on invoice #{a.TransactionId} (application #{a.Id} v{Math.Max(1, a.Version)}, backfill)", ct, a.On));
            }

            var combos = await _db.Set<ComboApplication>().AsNoTracking().Where(a => a.Amount > 0).OrderBy(a => a.Id).ToListAsync(ct);
            foreach (var a in combos)
            {
                if (knownSet.Contains((PromotionPostingType.PromotionCombo, a.Id))) continue;
                await Run(PostAsync(PromotionPostingType.PromotionCombo, a.Id, Math.Max(1, a.Version), -a.Amount, a.TransactionId, null,
                    $"Combo discount {a.Amount:F2} applied on invoice #{a.TransactionId} (application #{a.Id} v{Math.Max(1, a.Version)}, backfill)", ct, a.On));
            }

            var still = attempted - posted;
            return new BaseResponse<PromotionRepostResultDto>(true, null,
                attempted == 0 ? "Nothing to backfill" : $"{posted} of {attempted} historic posting(s) booked" + (still > 0 ? $", {still} pending" : ""),
                new PromotionRepostResultDto(attempted, posted, still, results));
        }

        // ── reports ──────────────────────────────────────────────────────
        public async Task<BaseResponse<GiftCardLiabilityReportDto>> GetGiftCardLiabilityReportAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var cards = await _db.Set<GiftCard>().AsNoTracking()
                .Where(c => c.Balance > 0 && c.Status != GiftCardStatus.Disabled)
                .Select(c => new { c.Balance, c.Status, c.ExpiresOn }).ToListAsync(ct);
            var active = cards.Where(c => c.Status == GiftCardStatus.Active && (!c.ExpiresOn.HasValue || c.ExpiresOn.Value > now)).ToList();
            var expired = cards.Where(c => c.Status == GiftCardStatus.Expired || (c.ExpiresOn.HasValue && c.ExpiresOn.Value <= now)).ToList();

            decimal ledger = 0m;
            var liability = await FindAccountAsync(LiabilityAccountNumber, ct);
            if (liability is not null)
            {
                var sums = await _db.Set<JournalEntryLine>().AsNoTracking()
                    .Where(l => l.AccountId == liability.Id && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided)
                    .GroupBy(_ => 1).Select(g => new { Dr = g.Sum(l => l.DebitAmount), Cr = g.Sum(l => l.CreditAmount) })
                    .FirstOrDefaultAsync(ct);
                ledger = Math.Round((sums?.Cr ?? 0m) - (sums?.Dr ?? 0m), 2);
            }

            var pendingRows = await _db.Set<PromotionAccountingPosting>().AsNoTracking()
                .Where(p => p.Status == PromotionPostingStatus.Pending && (p.ReferenceType == PromotionPostingType.GiftCardLoad
                    || p.ReferenceType == PromotionPostingType.GiftCardRedeem || p.ReferenceType == PromotionPostingType.GiftCardRefund))
                .Select(p => new { p.ReferenceType, p.Amount }).ToListAsync(ct);
            var pendingAmount = Math.Round(pendingRows.Sum(p => p.ReferenceType == PromotionPostingType.GiftCardRedeem ? -Math.Abs(p.Amount) : Math.Abs(p.Amount)), 2);

            var activeBalance = Math.Round(active.Sum(c => c.Balance), 2);
            var difference = Math.Round(activeBalance - ledger, 2);
            string? note = null;
            if (liability is null) note = $"Account {LiabilityAccountNumber} does not exist yet; it is created on the first gift card posting.";
            else if (difference != 0m && pendingRows.Count > 0) note = $"{pendingRows.Count} gift card posting(s) are pending ({pendingAmount:F2}); repost them to reconcile.";
            else if (difference != 0m && expired.Count > 0) note = "Expired cards still hold value in the ledger (breakage) and manual adjustments are not booked automatically.";
            else if (difference != 0m) note = "Cards issued before accounting was enabled are not in the ledger; run the backfill or post an opening entry.";

            return new BaseResponse<GiftCardLiabilityReportDto>(true, null, "OK", new GiftCardLiabilityReportDto(
                now, active.Count, activeBalance, expired.Count, Math.Round(expired.Sum(c => c.Balance), 2),
                ledger, difference, pendingRows.Count, pendingAmount, LiabilityAccountNumber, note));
        }

        public async Task<BaseResponse<PromotionDiscountsReportDto>> GetDiscountsReportAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var f = from.HasValue ? AsUtc(from.Value) : DateTime.UtcNow.Date.AddDays(-30);
            var t = to.HasValue ? AsUtc(to.Value) : DateTime.UtcNow;
            if (t < f) (f, t) = (t, f);

            var rows = await _db.Set<PromotionAccountingPosting>().AsNoTracking()
                .Where(p => p.On >= f && p.On <= t)
                .Select(p => new { p.ReferenceType, p.Amount, p.Status }).ToListAsync(ct);

            var kinds = new[] { "Coupon", "HappyHour", "Combo" }.Select(kind =>
            {
                var type = PromotionPostingType.ForPromotionKind(kind);
                var k = rows.Where(r => r.ReferenceType == type).ToList();
                var given = Math.Round(k.Where(r => r.Amount < 0).Sum(r => -r.Amount), 2);
                var refunded = Math.Round(k.Where(r => r.Amount > 0).Sum(r => r.Amount), 2);
                var postedNet = Math.Round(k.Where(r => r.Status == PromotionPostingStatus.Posted).Sum(r => -r.Amount), 2);
                var pendingNet = Math.Round(k.Where(r => r.Status == PromotionPostingStatus.Pending).Sum(r => -r.Amount), 2);
                return new PromotionDiscountKindDto(kind, k.Count, k.Count(r => r.Amount < 0), k.Count(r => r.Amount > 0),
                    given, refunded, Math.Round(given - refunded, 2), postedNet, pendingNet, k.Count(r => r.Status == PromotionPostingStatus.Pending));
            }).ToList();

            var redeemed = Math.Round(rows.Where(r => r.ReferenceType == PromotionPostingType.GiftCardRedeem).Sum(r => Math.Abs(r.Amount)), 2);

            return new BaseResponse<PromotionDiscountsReportDto>(true, null, "OK", new PromotionDiscountsReportDto(
                f, t, kinds, kinds.Sum(k => k.NetDiscount), kinds.Sum(k => k.PostedAmount), kinds.Sum(k => k.PendingAmount),
                redeemed, DiscountAccountNumber));
        }

        private static DateTime AsUtc(DateTime d) => d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };
    }
}
