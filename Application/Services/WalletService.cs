using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    /// <summary>
    /// The single owner of wallet money movement. Every mutation writes the
    /// wallet balance, a ledger row, and (except spends) a journal entry in
    /// one operation — so the wallet, its history, and account 2100 stay in
    /// lockstep.
    ///
    /// Accounting model:
    ///   top-up   → DR 1000 Cash (+ DR 4905 bonus) / CR 2100 Customer Wallets
    ///   spend    → handled by the SALE's journal entry (DR 2100 portion)
    ///   refund   → DR 2100 / CR 1000  (cash physically leaves the drawer)
    ///   adjust   → DR/CR 2100 against 4905 (book correction, no cash moved)
    /// </summary>
    public class WalletService : IWalletService
    {
        public const string CashAccount = "1000";
        public const string WalletLiabilityAccount = "2100";
        public const string BonusAccount = "4905";

        private readonly IBaseRepository<Wallet> _repo;
        private readonly IBaseRepository<WalletTransaction> _repoTxn;
        private readonly IBaseRepository<WalletBonusTier> _repoTier;
        private readonly IBaseRepository<Account> _repoAccount;
        private readonly UserManager<AppUser> _userManager;
        private readonly IJournalService _journal;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<WalletService> _logger;

        public WalletService(
            IBaseRepository<Wallet> repo,
            IBaseRepository<WalletTransaction> repoTxn,
            IBaseRepository<WalletBonusTier> repoTier,
            IBaseRepository<Account> repoAccount,
            UserManager<AppUser> userManager,
            IJournalService journal,
            IUnitOfWork uow,
            ILogger<WalletService> logger)
        {
            _repo = repo;
            _repoTxn = repoTxn;
            _repoTier = repoTier;
            _repoAccount = repoAccount;
            _userManager = userManager;
            _journal = journal;
            _uow = uow;
            _logger = logger;
        }

        // ── Reads ────────────────────────────────────────────────────────

        public async Task<WalletSummaryDto> GetSummaryAsync(int userId, int recentCount = 10, CancellationToken ct = default)
        {
            var wallet = await GetOrCreateAsync(userId, ct);

            var recent = await _repoTxn.Query()
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.CreatedOn).ThenByDescending(t => t.Id)
                .Take(Math.Clamp(recentCount, 1, 50))
                .Select(t => new WalletTransactionDto(
                    t.Id, t.Type, t.Amount, t.BalanceAfter, t.Method,
                    t.TransactionRecordId, t.Notes, t.CreatedBy, t.CreatedOn))
                .ToListAsync(ct);

            var user = await _userManager.FindByIdAsync(userId.ToString());
            var name = user == null ? null
                : (!string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName
                    : $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } full ? full
                    : user.UserName);

            return new WalletSummaryDto(
                new WalletDto(wallet.Id, wallet.UserId, name, wallet.Balance,
                              wallet.IsActive, wallet.CreatedOn, wallet.ModifiedOn),
                recent);
        }

        public async Task<Dictionary<int, decimal>> GetBalancesAsync(IReadOnlyList<int> userIds, CancellationToken ct = default)
        {
            if (userIds.Count == 0) return new Dictionary<int, decimal>();
            return await _repo.Query()
                .Where(w => userIds.Contains(w.UserId))
                .ToDictionaryAsync(w => w.UserId, w => w.Balance, ct);
        }

        public async Task<PaginatedResponse<WalletTransactionDto>> GetHistoryAsync(
            int userId, int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var wallet = await _repo.Query().FirstOrDefaultAsync(w => w.UserId == userId, ct);
            if (wallet is null)
                return new PaginatedResponse<WalletTransactionDto>(0, new List<WalletTransactionDto>(), page, pageSize);

            var q = _repoTxn.Query().Where(t => t.WalletId == wallet.Id);
            var total = await q.CountAsync(ct);

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var rows = await q
                .OrderByDescending(t => t.CreatedOn).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => new WalletTransactionDto(
                    t.Id, t.Type, t.Amount, t.BalanceAfter, t.Method,
                    t.TransactionRecordId, t.Notes, t.CreatedBy, t.CreatedOn))
                .ToListAsync(ct);

            return new PaginatedResponse<WalletTransactionDto>(total, rows, page, pageSize);
        }

        public async Task<WalletMovementsPageDto> GetMovementsAsync(
            DateTime? from = null, DateTime? to = null, string? type = null, string? method = null,
            int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var q = _repoTxn.Query().AsNoTracking();

            // Bounds arrive as ISO datetimes from the UI; re-stamp to UTC so
            // Npgsql accepts them against the timestamptz column.
            if (from.HasValue)
            {
                var f = AsUtc(from.Value);
                q = q.Where(t => t.CreatedOn >= f);
            }
            if (to.HasValue)
            {
                var t2 = AsUtc(to.Value);
                q = q.Where(t => t.CreatedOn < t2);
            }
            if (!string.IsNullOrWhiteSpace(type))
                q = q.Where(t => t.Type == type);
            if (!string.IsNullOrWhiteSpace(method))
                q = q.Where(t => t.Method == method);

            // Period totals over the FULL filtered set (not the page) — this
            // is the number the cashier reconciles the drawer against.
            var sums = await q
                .GroupBy(t => new { t.Type, t.Method })
                .Select(g => new { g.Key.Type, g.Key.Method, Sum = g.Sum(x => x.Amount), Count = g.Count() })
                .ToListAsync(ct);

            decimal SumOf(string ty, string? me = null) => sums
                .Where(s => s.Type == ty && (me == null || s.Method == me))
                .Sum(s => s.Sum);

            var cashIn = SumOf("TopUp", "Cash");
            var whishIn = SumOf("TopUp", "Whish");
            var cardIn = SumOf("TopUp", "Card");
            var totalTopUps = SumOf("TopUp");
            var refunded = SumOf("Refund");

            var summary = new WalletMovementsSummaryDto(
                CashIn: cashIn,
                WhishIn: whishIn,
                CardIn: cardIn,
                TotalTopUps: totalTopUps,
                BonusGiven: SumOf("Bonus"),
                Spent: SumOf("Spend"),
                RefundedCashOut: refunded,
                NetCashImpact: cashIn - refunded,
                TopUpCount: sums.Where(s => s.Type == "TopUp").Sum(s => s.Count));

            var totalCount = await q.CountAsync(ct);

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var rows = await q
                .OrderByDescending(t => t.CreatedOn).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => new WalletMovementDto(
                    t.Id,
                    t.CreatedOn,
                    t.Type,
                    t.Amount,
                    t.Method,
                    t.Wallet.UserId,
                    t.Wallet.User != null
                        ? (t.Wallet.User.DisplayName
                            ?? (((t.Wallet.User.FirstName ?? "") + " " + (t.Wallet.User.LastName ?? "")).Trim() != ""
                                ? ((t.Wallet.User.FirstName ?? "") + " " + (t.Wallet.User.LastName ?? "")).Trim()
                                : t.Wallet.User.UserName))
                        : null,
                    t.CreatedBy,
                    t.Notes,
                    t.TransactionRecordId,
                    t.BalanceAfter))
                .ToListAsync(ct);

            return new WalletMovementsPageDto(summary, totalCount, rows, page, pageSize);
        }

        private static DateTime AsUtc(DateTime d) => d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };

        // ── Top-up ───────────────────────────────────────────────────────

        public async Task<BaseResponse<WalletTopUpResultDto>> TopUpAsync(
            int userId, WalletTopUpRequestDto req, string actor, CancellationToken ct = default)
        {
            var amount = Math.Round(req.Amount, 2);
            if (amount <= 0)
                return new BaseResponse<WalletTopUpResultDto>(false, "Invalid amount", "Top-up amount must be positive.", null);
            if (amount > 10_000)
                return new BaseResponse<WalletTopUpResultDto>(false, "Amount too large", "Top-ups above $10,000 are not allowed.", null);

            var wallet = await GetOrCreateAsync(userId, ct, track: true);
            if (!wallet.IsActive)
                return new BaseResponse<WalletTopUpResultDto>(false, "Wallet disabled", "This wallet has been disabled by an admin.", null);

            // Best tier = the highest threshold this top-up reaches. Tiers
            // never stack — $150 with tiers at $50(+5%) and $100(+10%) gets
            // exactly +10%.
            var tier = await _repoTier.Query()
                .Where(t => t.IsActive && t.MinAmount <= amount)
                .OrderByDescending(t => t.MinAmount)
                .FirstOrDefaultAsync(ct);

            var bonusPct = tier?.BonusPercent ?? 0m;
            var bonus = Math.Round(amount * bonusPct / 100m, 2);

            var method = string.IsNullOrWhiteSpace(req.Method) ? "Cash" : req.Method.Trim();

            // Ledger: the paid amount and the gift are separate rows, so the
            // history reads "TopUp $100" + "Bonus $10" instead of a mystery $110.
            wallet.Balance += amount;
            var topUpRow = new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = "TopUp",
                Amount = amount,
                // Snapshot after THIS row only — the bonus row carries the
                // final figure, so a rebuild-from-ledger audit adds up.
                BalanceAfter = wallet.Balance,
                Method = method,
                Notes = req.Notes,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow,
            };
            await _repoTxn.AddAsync(topUpRow, ct);

            if (bonus > 0)
            {
                wallet.Balance += bonus;
                await _repoTxn.AddAsync(new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = "Bonus",
                    Amount = bonus,
                    BalanceAfter = wallet.Balance,
                    Notes = $"+{bonusPct:0.##}% tier bonus on {amount:0.00} top-up",
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow,
                }, ct);
            }

            wallet.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);

            // Books: money in, liability up. Never blocks the top-up — the
            // cash is already in the drawer; a ledger gap is repairable, a
            // rejected customer is not.
            await PostJournalAsync(
                referenceType: "WalletTopUp",
                referenceId: topUpRow.Id,
                description: $"Wallet top-up #{topUpRow.Id} — user {userId} ({method})",
                lines: bonus > 0
                    ? new[] { (CashAccount, amount, 0m, $"Top-up received ({method})"),
                              (BonusAccount, bonus, 0m, $"Tier bonus +{bonusPct:0.##}%"),
                              (WalletLiabilityAccount, 0m, amount + bonus, "Wallet credit") }
                    : new[] { (CashAccount, amount, 0m, $"Top-up received ({method})"),
                              (WalletLiabilityAccount, 0m, amount, "Wallet credit") },
                ct);

            _logger.LogInformation(
                "Wallet top-up: user {UserId} +{Amount} (+{Bonus} bonus) by {Actor}. Balance {Balance}",
                userId, amount, bonus, actor, wallet.Balance);

            return new BaseResponse<WalletTopUpResultDto>(true, null, "Top-up complete",
                new WalletTopUpResultDto(wallet.Id, amount, bonus, bonusPct, wallet.Balance));
        }

        // ── Admin adjust / refund ────────────────────────────────────────

        public async Task<BaseResponse<WalletDto>> AdjustAsync(
            int userId, WalletAdjustRequestDto req, string actor, CancellationToken ct = default)
        {
            var delta = Math.Round(req.Delta, 2);
            if (delta == 0)
                return new BaseResponse<WalletDto>(false, "Invalid amount", "Delta cannot be zero.", null);
            if (string.IsNullOrWhiteSpace(req.Reason))
                return new BaseResponse<WalletDto>(false, "Reason required", "Every wallet adjustment needs a reason for the audit trail.", null);

            var wallet = await GetOrCreateAsync(userId, ct, track: true);

            if (delta < 0 && wallet.Balance + delta < 0)
                return new BaseResponse<WalletDto>(false, "Insufficient balance",
                    $"Wallet has {wallet.Balance:0.00}; cannot remove {Math.Abs(delta):0.00}.", null);

            var isRefund = delta < 0 && req.CashOut;

            wallet.Balance += delta;
            wallet.ModifiedOn = DateTime.UtcNow;

            var row = new WalletTransaction
            {
                WalletId = wallet.Id,
                // "Deduction" for a downward correction — the ledger stores
                // absolute amounts, so the type must carry the direction or
                // the history can't tell +15 from −15.
                Type = isRefund ? "Refund" : delta > 0 ? "Adjustment" : "Deduction",
                Amount = Math.Abs(delta),
                BalanceAfter = wallet.Balance,
                Method = isRefund ? "Cash" : null,
                Notes = req.Reason.Trim(),
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow,
            };
            await _repoTxn.AddAsync(row, ct);
            await _uow.SaveChangesAsync(ct);

            // Refund: cash leaves the drawer. Correction: no cash moved, so
            // the counterpart is the bonus/goodwill account.
            var abs = Math.Abs(delta);
            (string, decimal, decimal, string)[] lines;
            if (isRefund)
                lines = new[] { (WalletLiabilityAccount, abs, 0m, "Wallet refund"),
                                (CashAccount, 0m, abs, "Cash paid out") };
            else if (delta < 0)
                lines = new[] { (WalletLiabilityAccount, abs, 0m, $"Correction: {req.Reason.Trim()}"),
                                (BonusAccount, 0m, abs, "Correction") };
            else
                lines = new[] { (BonusAccount, abs, 0m, $"Goodwill credit: {req.Reason.Trim()}"),
                                (WalletLiabilityAccount, 0m, abs, "Wallet credit") };

            await PostJournalAsync(
                referenceType: isRefund ? "WalletRefund" : "WalletAdjustment",
                referenceId: row.Id,
                description: $"Wallet {(isRefund ? "refund" : "adjustment")} #{row.Id} — user {userId}: {req.Reason.Trim()}",
                lines, ct);

            _logger.LogInformation(
                "Wallet {Kind}: user {UserId} {Delta:+0.00;-0.00} by {Actor} ({Reason}). Balance {Balance}",
                isRefund ? "refund" : "adjustment", userId, delta, actor, req.Reason, wallet.Balance);

            var summary = await GetSummaryAsync(userId, 1, ct);
            return new BaseResponse<WalletDto>(true, null, "Wallet updated", summary.Wallet);
        }

        // ── Spend (called from checkout) ─────────────────────────────────

        public async Task<BaseResponse<decimal>> SpendAsync(
            int userId, decimal amount, int transactionRecordId, string actor, CancellationToken ct = default)
        {
            amount = Math.Round(amount, 2);
            if (amount <= 0)
                return new BaseResponse<decimal>(false, "Invalid amount", "Wallet amount must be positive.", 0);

            var wallet = await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(w => w.UserId == userId, ct);

            if (wallet is null || !wallet.IsActive)
                return new BaseResponse<decimal>(false, "No wallet", "This client has no active wallet.", 0);

            if (wallet.Balance < amount)
                return new BaseResponse<decimal>(false, "Insufficient balance",
                    $"Wallet has {wallet.Balance:0.00}, needs {amount:0.00}.", wallet.Balance);

            wallet.Balance -= amount;
            wallet.ModifiedOn = DateTime.UtcNow;

            await _repoTxn.AddAsync(new WalletTransaction
            {
                WalletId = wallet.Id,
                Type = "Spend",
                Amount = amount,
                BalanceAfter = wallet.Balance,
                TransactionRecordId = transactionRecordId,
                Notes = $"Paid invoice #{transactionRecordId}",
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow,
            }, ct);

            // No SaveChanges here on purpose — the caller (checkout) commits
            // this together with the invoice close, so a failed close can't
            // leave money deducted for nothing. No journal entry either: the
            // sale's own entry books DR 2100 for the wallet portion.
            _logger.LogInformation(
                "Wallet spend: user {UserId} -{Amount} on trx {TrxId} by {Actor}. Balance {Balance}",
                userId, amount, transactionRecordId, actor, wallet.Balance);

            return new BaseResponse<decimal>(true, null, "Wallet charged", wallet.Balance);
        }

        // ── Bonus tiers ──────────────────────────────────────────────────

        public async Task<List<WalletBonusTierDto>> GetTiersAsync(bool includeInactive = false, CancellationToken ct = default)
        {
            var q = _repoTier.Query();
            if (!includeInactive) q = q.Where(t => t.IsActive);
            return await q.OrderBy(t => t.MinAmount)
                .Select(t => new WalletBonusTierDto(t.Id, t.MinAmount, t.BonusPercent, t.IsActive))
                .ToListAsync(ct);
        }

        public async Task<WalletBonusTierDto> CreateTierAsync(WalletBonusTierUpsertDto dto, CancellationToken ct = default)
        {
            ValidateTier(dto);
            var e = new WalletBonusTier
            {
                MinAmount = Math.Round(dto.MinAmount, 2),
                BonusPercent = Math.Round(dto.BonusPercent, 2),
                IsActive = dto.IsActive,
                CreatedOn = DateTime.UtcNow,
            };
            await _repoTier.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return new WalletBonusTierDto(e.Id, e.MinAmount, e.BonusPercent, e.IsActive);
        }

        public async Task<bool> UpdateTierAsync(int id, WalletBonusTierUpsertDto dto, CancellationToken ct = default)
        {
            ValidateTier(dto);
            var e = await _repoTier.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;
            e.MinAmount = Math.Round(dto.MinAmount, 2);
            e.BonusPercent = Math.Round(dto.BonusPercent, 2);
            e.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteTierAsync(int id, CancellationToken ct = default)
        {
            var e = await _repoTier.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;
            _repoTier.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        private static void ValidateTier(WalletBonusTierUpsertDto dto)
        {
            if (dto.MinAmount < 0) throw new ArgumentException("Minimum amount cannot be negative.");
            if (dto.BonusPercent < 0 || dto.BonusPercent > 100)
                throw new ArgumentException("Bonus percent must be between 0 and 100.");
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private async Task<Wallet> GetOrCreateAsync(int userId, CancellationToken ct, bool track = false)
        {
            var wallet = await _repo.Query(asNoTracking: !track)
                .FirstOrDefaultAsync(w => w.UserId == userId, ct);
            if (wallet is not null) return wallet;

            var userExists = await _userManager.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
            if (!userExists) throw new ArgumentException($"User {userId} does not exist.");

            wallet = new Wallet { UserId = userId, Balance = 0, IsActive = true, CreatedOn = DateTime.UtcNow };
            await _repo.AddAsync(wallet, ct);
            await _uow.SaveChangesAsync(ct);
            return wallet;
        }

        /// <summary>
        /// Books a wallet movement. Log-and-continue on failure — the money
        /// movement is already committed; a missing entry surfaces in the
        /// books audit rather than as a failed till operation.
        /// </summary>
        private async Task PostJournalAsync(
            string referenceType, int referenceId, string description,
            (string accountNumber, decimal debit, decimal credit, string note)[] lines,
            CancellationToken ct)
        {
            try
            {
                var numbers = lines.Select(l => l.accountNumber).Distinct().ToList();
                var accounts = await _repoAccount.Query()
                    .Where(a => numbers.Contains(a.AccountNumber) && a.IsActive)
                    .ToDictionaryAsync(a => a.AccountNumber, ct);

                var missing = numbers.Where(n => !accounts.ContainsKey(n)).ToList();
                if (missing.Count > 0)
                {
                    _logger.LogWarning(
                        "Wallet journal skipped for {RefType} #{RefId}: account(s) {Missing} not found — run db-migrations/2026-08-customer-wallets.sql",
                        referenceType, referenceId, string.Join(", ", missing));
                    return;
                }

                var dtoLines = lines
                    .Select(l => new JournalEntryLineCreateDto(accounts[l.accountNumber].Id, l.debit, l.credit, l.note))
                    .ToList();

                var created = await _journal.CreateJournalEntryAsync(
                    new JournalEntryCreateDto(DateTime.UtcNow, description, referenceType, referenceId, dtoLines),
                    null, ct);

                if (created.Success)
                    await _journal.PostJournalEntryAsync(created.Data!.Id, null, ct);
                else
                    _logger.LogWarning("Wallet journal for {RefType} #{RefId} not created: {Error}",
                        referenceType, referenceId, created.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wallet journal failed for {RefType} #{RefId}", referenceType, referenceId);
            }
        }
    }
}
