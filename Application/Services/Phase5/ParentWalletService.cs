using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    /// <summary>Read-only hook used by WalletService.SpendAsync: refuses a spend over the parent's daily limit.</summary>
    public interface IParentSpendGuard
    {
        Task<(bool Allowed, string? Message)> CheckAsync(int userId, decimal amount, CancellationToken ct = default);
    }

    public interface IParentWalletService
    {
        Task<List<ParentLinkDto>> ListAsync(CancellationToken ct = default);
        Task<BaseResponse<ParentLinkDto>> UpsertAsync(ParentLinkUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<ParentViewDto?> PublicViewAsync(string token, CancellationToken ct = default);
        Task<int> NotifySessionEndsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Parent controls on a child's wallet: daily spend limit (enforced on every
    /// wallet payment), alerts to the parent on spend, low balance and when a game
    /// session ends, and a read-only page for the parent.
    /// </summary>
    public class ParentWalletService : IParentWalletService, IParentSpendGuard
    {
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly IOwnerNotifier _notify;
        private readonly IEmailSender _email;
        private readonly ILogger<ParentWalletService> _log;

        public ParentWalletService(ApplicationDbContext db, IIntegrationSettingsService settings, IOwnerNotifier notify, IEmailSender email, ILogger<ParentWalletService> log)
        { _db = db; _settings = settings; _notify = notify; _email = email; _log = log; }

        private async Task<DateTime> TodayStartAsync(CancellationToken ct) => VenueClock.TodayStartUtc(VenueClock.Zone(await _settings.GetRawAsync("Venue.TimeZone", ct)));

        private async Task<decimal> SpentTodayAsync(int userId, CancellationToken ct)
        {
            var start = await TodayStartAsync(ct);
            return await (from t in _db.Set<WalletTransaction>().AsNoTracking()
                          join w in _db.Set<Wallet>().AsNoTracking() on t.WalletId equals w.Id
                          where w.UserId == userId && t.Type == "Spend" && t.CreatedOn >= start
                          select (decimal?)t.Amount).SumAsync(ct) ?? 0;
        }

        public async Task<(bool Allowed, string? Message)> CheckAsync(int userId, decimal amount, CancellationToken ct = default)
        {
            var link = await _db.Set<ParentLink>().AsNoTracking().FirstOrDefaultAsync(l => l.ChildUserId == userId && l.IsActive, ct);
            if (link?.DailyLimit is not decimal limit) return (true, null);
            var spent = await SpentTodayAsync(userId, ct);
            if (spent + amount > limit)
                return (false, $"Daily limit set by the parent: ${limit:0.00}. Already spent ${spent:0.00} today, ${Math.Max(0, limit - spent):0.00} left.");
            return (true, null);
        }

        private async Task<string> ChildNameAsync(int userId, CancellationToken ct)
            => await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName ?? u.FirstName ?? u.UserName ?? "Your child").FirstOrDefaultAsync(ct) ?? "Your child";

        private async Task TellParentAsync(ParentLink link, string text, CancellationToken ct)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(link.ParentTelegramChatId)) await _notify.SendTelegramAsync(link.ParentTelegramChatId, text, ct);
                if (!string.IsNullOrWhiteSpace(link.ParentPhone)) await _notify.SendWhatsAppTextAsync(link.ParentPhone, text, ct);
                if (!string.IsNullOrWhiteSpace(link.ParentEmail)) await _email.SendAsync(new[] { link.ParentEmail }, "Axis wallet", text, false, null, ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Parent alert failed for link {Id}", link.Id); }
        }

        public async Task<List<ParentLinkDto>> ListAsync(CancellationToken ct = default)
        {
            var links = await _db.Set<ParentLink>().AsNoTracking().OrderBy(l => l.ParentName).ToListAsync(ct);
            var ids = links.Select(l => l.ChildUserId).ToList();
            var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, Name = u.DisplayName ?? u.FirstName ?? u.UserName }).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
            var balances = await _db.Set<Wallet>().AsNoTracking().Where(w => ids.Contains(w.UserId)).ToDictionaryAsync(w => w.UserId, w => w.Balance, ct);
            var result = new List<ParentLinkDto>();
            foreach (var l in links)
                result.Add(new(l.Id, l.ChildUserId, users.GetValueOrDefault(l.ChildUserId) ?? $"#{l.ChildUserId}", l.ParentName, l.ParentPhone, l.ParentTelegramChatId, l.ParentEmail,
                    l.DailyLimit, l.LowBalanceThreshold, l.NotifyOnSpend, l.NotifyOnSessionEnd, l.IsActive, l.Token, balances.GetValueOrDefault(l.ChildUserId), await SpentTodayAsync(l.ChildUserId, ct)));
            return result;
        }

        public async Task<BaseResponse<ParentLinkDto>> UpsertAsync(ParentLinkUpsertDto dto, CancellationToken ct = default)
        {
            if (!await _db.Users.AnyAsync(u => u.Id == dto.ChildUserId, ct)) return new(false, "NotFound", "Child account not found.");
            if (string.IsNullOrWhiteSpace(dto.ParentName)) return new(false, "Validation", "Parent name is required.");
            if (string.IsNullOrWhiteSpace(dto.ParentPhone) && string.IsNullOrWhiteSpace(dto.ParentTelegramChatId) && string.IsNullOrWhiteSpace(dto.ParentEmail))
                return new(false, "Validation", "Give the parent's phone, Telegram chat id or email so alerts can reach them.");
            if (dto.DailyLimit is < 0 || dto.LowBalanceThreshold is < 0) return new(false, "Validation", "Amounts cannot be negative.");
            var l = await _db.Set<ParentLink>().FirstOrDefaultAsync(x => x.ChildUserId == dto.ChildUserId, ct);
            if (l is null)
            {
                var lastTx = await _db.Set<TransactionRecord>().Where(t => t.UserId == dto.ChildUserId).MaxAsync(t => (int?)t.Id, ct) ?? 0;
                var lastWalletTx = await (from t in _db.Set<WalletTransaction>() join w in _db.Set<Wallet>() on t.WalletId equals w.Id where w.UserId == dto.ChildUserId select (int?)t.Id).MaxAsync(ct) ?? 0;
                l = new ParentLink { ChildUserId = dto.ChildUserId, Token = TournamentService.NewToken(), LastNotifiedTransactionId = lastTx, LastNotifiedWalletTxId = lastWalletTx };
                _db.Add(l);
            }
            l.ParentName = dto.ParentName.Trim(); l.ParentPhone = Clean(dto.ParentPhone); l.ParentTelegramChatId = Clean(dto.ParentTelegramChatId); l.ParentEmail = Clean(dto.ParentEmail);
            l.DailyLimit = dto.DailyLimit; l.LowBalanceThreshold = dto.LowBalanceThreshold; l.NotifyOnSpend = dto.NotifyOnSpend; l.NotifyOnSessionEnd = dto.NotifyOnSessionEnd; l.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Parent controls saved.", (await ListAsync(ct)).First(x => x.Id == l.Id));
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var l = await _db.Set<ParentLink>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return false;
            _db.Remove(l);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<ParentViewDto?> PublicViewAsync(string token, CancellationToken ct = default)
        {
            var l = await _db.Set<ParentLink>().AsNoTracking().FirstOrDefaultAsync(x => x.Token == token && x.IsActive, ct);
            if (l is null) return null;
            var wallet = await _db.Set<Wallet>().AsNoTracking().FirstOrDefaultAsync(w => w.UserId == l.ChildUserId, ct);
            var spent = await SpentTodayAsync(l.ChildUserId, ct);
            var recent = wallet is null ? new List<ParentViewTxDto>() : await _db.Set<WalletTransaction>().AsNoTracking().Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.Id).Take(15).Select(t => new ParentViewTxDto(t.CreatedOn, t.Type, t.Amount, t.BalanceAfter, t.Notes)).ToListAsync(ct);
            return new(await ChildNameAsync(l.ChildUserId, ct), wallet?.Balance ?? 0, spent, l.DailyLimit, l.DailyLimit is decimal d ? Math.Max(0, d - spent) : null, recent);
        }

        /// <summary>
        /// Job (every 5 minutes): tell parents about new wallet spends, a low balance
        /// (once per venue day) and finished game sessions. Runs outside checkout so it
        /// never commits a payment early.
        /// </summary>
        public async Task<int> NotifySessionEndsAsync(CancellationToken ct = default)
        {
            var links = await _db.Set<ParentLink>().Where(l => l.IsActive).ToListAsync(ct);
            var start = await TodayStartAsync(ct);
            var sent = 0;
            foreach (var l in links)
            {
                var child = await ChildNameAsync(l.ChildUserId, ct);
                var wallet = await _db.Set<Wallet>().AsNoTracking().FirstOrDefaultAsync(w => w.UserId == l.ChildUserId, ct);
                var bal = wallet?.Balance ?? 0;
                if (wallet is not null)
                {
                    var spends = await _db.Set<WalletTransaction>().AsNoTracking().Where(t => t.WalletId == wallet.Id && t.Id > l.LastNotifiedWalletTxId)
                        .OrderBy(t => t.Id).Select(t => new { t.Id, t.Type, t.Amount, t.BalanceAfter }).ToListAsync(ct);
                    if (l.NotifyOnSpend)
                        foreach (var s in spends.Where(s => s.Type == "Spend")) { await TellParentAsync(l, $"{child} spent ${s.Amount:0.00} at Axis. Balance: ${s.BalanceAfter:0.00}.", ct); sent++; }
                    if (spends.Count > 0) l.LastNotifiedWalletTxId = spends.Max(s => s.Id);
                }
                if (l.LowBalanceThreshold is decimal th && wallet is not null && bal < th && (l.LastLowBalanceAlertOn is null || l.LastLowBalanceAlertOn < start))
                {
                    await TellParentAsync(l, $"{child}'s Axis wallet is low: ${bal:0.00} left. Top up at the counter.", ct);
                    l.LastLowBalanceAlertOn = DateTime.UtcNow; sent++;
                }
                if (l.NotifyOnSessionEnd)
                {
                    var ended = await _db.Set<TransactionRecord>().AsNoTracking()
                        .Where(t => t.UserId == l.ChildUserId && t.GameId != null && t.StatusId == 6 && t.Id > l.LastNotifiedTransactionId)
                        .OrderBy(t => t.Id).Select(t => new { t.Id, t.TotalPrice, t.Hours, Game = t.Game!.Name }).ToListAsync(ct);
                    foreach (var e in ended) { await TellParentAsync(l, $"{child} finished playing {e.Game} ({e.Hours:0.#} h, ${e.TotalPrice:0.00}). Wallet balance: ${bal:0.00}.", ct); sent++; }
                    if (ended.Count > 0) l.LastNotifiedTransactionId = ended.Max(e => e.Id);
                }
            }
            await _db.SaveChangesAsync(ct);
            return sent;
        }
    }
}
