using System.Security.Cryptography;
using System.Text;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase4
{
    /// <summary>
    /// Card payments on a terminal against an open invoice. Same shape as the
    /// Whish flow: start → poll (or provider callback) → on approval a "Card"
    /// tender is recorded once and the invoice closes when nothing remains.
    /// </summary>
    public class CardTerminalService : ICardTerminalService
    {
        public const string Currency = "USD";
        public const string TenderMethod = "Card";
        public const string ReferencePrefix = "Card:";
        public const string CallbackSecretKey = "CardTerminal.Cloud.WebhookSecret";
        private const int OpenInvoiceStatus = 7;
        private const int ClosedInvoiceStatus = 6;
        public static readonly TimeSpan AutoLifetime = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan ManualLifetime = TimeSpan.FromMinutes(30);

        private readonly ApplicationDbContext _db;
        private readonly IEnumerable<ICardTerminalProvider> _providers;
        private readonly ITenderService _tenders;
        private readonly ITransactionRecordService _transactions;
        private readonly IIntegrationSettingsService _settings;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<CardTerminalService> _log;

        public CardTerminalService(ApplicationDbContext db, IEnumerable<ICardTerminalProvider> providers, ITenderService tenders,
            ITransactionRecordService transactions, IIntegrationSettingsService settings, ICurrentUserAccessor user, ILogger<CardTerminalService> log)
        { _db = db; _providers = providers; _tenders = tenders; _transactions = transactions; _settings = settings; _user = user; _log = log; }

        private ICardTerminalProvider Provider(string name)
            => _providers.FirstOrDefault(p => p.Name == name) ?? _providers.First(p => p.Name == CardTerminalProviders.Manual);

        // ── Terminals ──────────────────────────────────────────────────
        public async Task<List<CardTerminalDto>> ListTerminalsAsync(bool includeInactive, CancellationToken ct = default)
        {
            var busy = await _db.Set<TerminalPayment>().AsNoTracking().Where(p => p.Status == TerminalPaymentStatuses.Pending)
                .Select(p => p.TerminalId).Distinct().ToListAsync(ct);
            return await _db.Set<CardTerminal>().AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.Name)
                .Select(t => new CardTerminalDto(t.Id, t.Name, t.Provider, t.ProviderTerminalId, t.BranchId, t.IsActive, busy.Contains(t.Id)))
                .ToListAsync(ct);
        }

        public async Task<BaseResponse<CardTerminalDto>> CreateTerminalAsync(CardTerminalUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err is not null) return new(false, "Validation", err);
            var t = new CardTerminal { CreatedBy = _user.UserName, CreatedOn = DateTime.UtcNow };
            Apply(t, dto);
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Terminal added.", (await ListTerminalsAsync(true, ct)).First(x => x.Id == t.Id));
        }

        public async Task<BaseResponse<CardTerminalDto>> UpdateTerminalAsync(int id, CardTerminalUpsertDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<CardTerminal>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return new(false, "NotFound", "Terminal not found.");
            var err = Validate(dto);
            if (err is not null) return new(false, "Validation", err);
            Apply(t, dto);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Terminal updated.", (await ListTerminalsAsync(true, ct)).First(x => x.Id == t.Id));
        }

        public async Task<BaseResponse<bool>> DeleteTerminalAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<CardTerminal>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return new(false, "NotFound", "Terminal not found.");
            if (await _db.Set<TerminalPayment>().AnyAsync(p => p.TerminalId == id, ct))
            {
                t.IsActive = false;
                await _db.SaveChangesAsync(ct);
                return new(true, null, "The terminal has payments on record, so it was deactivated instead of deleted.", false);
            }
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Terminal deleted.", true);
        }

        private static void Apply(CardTerminal t, CardTerminalUpsertDto d)
        {
            t.Name = d.Name.Trim();
            t.Provider = CardTerminalProviders.All.First(p => string.Equals(p, d.Provider, StringComparison.OrdinalIgnoreCase));
            t.ProviderTerminalId = string.IsNullOrWhiteSpace(d.ProviderTerminalId) ? null : d.ProviderTerminalId.Trim();
            t.BranchId = d.BranchId is > 0 ? d.BranchId : null;
            t.IsActive = d.IsActive;
        }

        private static string? Validate(CardTerminalUpsertDto d)
        {
            if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Trim().Length > 120) return "Name is required (max 120).";
            if (!CardTerminalProviders.All.Any(p => string.Equals(p, d.Provider, StringComparison.OrdinalIgnoreCase)))
                return $"Provider must be one of: {string.Join(", ", CardTerminalProviders.All)}.";
            if (string.Equals(d.Provider, CardTerminalProviders.CloudHttp, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(d.ProviderTerminalId))
                return "A CloudHttp terminal needs its terminal id (TID) at the provider.";
            if (d.ProviderTerminalId is { Length: > 80 }) return "Terminal id is limited to 80 characters.";
            return null;
        }

        // ── Payments ───────────────────────────────────────────────────
        public async Task<BaseResponse<TerminalPaymentDto>> StartAsync(TerminalStartDto dto, CancellationToken ct = default)
        {
            var terminal = await _db.Set<CardTerminal>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TerminalId, ct);
            if (terminal is null || !terminal.IsActive) return Fail("NotFound", "Terminal not found or inactive.");
            var tx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (tx is null) return Fail("NotFound", $"Invoice #{dto.TransactionId} not found.");
            if (tx.StatusId != OpenInvoiceStatus) return Fail("Closed", "Only open invoices can be paid by card here.");

            await ExpireStaleAsync(terminal.Id, ct);
            var busy = await _db.Set<TerminalPayment>().AsNoTracking()
                .FirstOrDefaultAsync(p => p.TerminalId == terminal.Id && p.Status == TerminalPaymentStatuses.Pending, ct);
            if (busy is not null)
                return busy.TransactionId == dto.TransactionId
                    ? new(true, null, "A payment for this invoice is already waiting on this terminal.", await ToDtoAsync(busy.Id, ct))
                    : Fail("Busy", $"Terminal is busy with invoice #{busy.TransactionId}. Finish or cancel that payment first.");

            var summary = await _tenders.SummaryAsync(dto.TransactionId, ct);
            var remaining = summary.Success && summary.Data is not null ? summary.Data.RemainingInBase : Math.Round(tx.TotalPrice, 2);
            if (remaining <= 0) return Fail("NothingToPay", "Nothing remains to pay on this invoice.");
            var amount = Math.Round(dto.Amount ?? remaining, 2);
            if (amount <= 0) return Fail("Validation", "Amount must be greater than zero.");
            if (amount > remaining) return Fail("Validation", $"Amount is more than the {remaining:0.00} {Currency} left to pay.");

            var p = new TerminalPayment
            {
                TransactionId = dto.TransactionId, TerminalId = terminal.Id, Amount = amount, Currency = Currency,
                ExternalId = $"axis-{dto.TransactionId}-{Guid.NewGuid():N}", Status = TerminalPaymentStatuses.Pending,
                CreatedOn = DateTime.UtcNow, CreatedBy = _user.UserName, BranchId = _user.BranchId ?? terminal.BranchId,
            };
            _db.Add(p);
            await _db.SaveChangesAsync(ct);

            var r = await Provider(terminal.Provider).StartAsync(terminal, p, ct);
            await ApplyResultAsync(p, r, ct);
            _log.LogInformation("Card payment {Id} on terminal {Terminal} for invoice {Tx}: {Amount} {Cur} → {Status}", p.Id, terminal.Name, p.TransactionId, p.Amount, p.Currency, p.Status);
            var data = await ToDtoAsync(p.Id, ct);
            return p.Status == TerminalPaymentStatuses.Failed ? new(false, "ProviderError", p.Message ?? "The terminal did not accept the payment.", data) : new(true, null, p.Message ?? "Sent to the terminal.", data);
        }

        public async Task<BaseResponse<TerminalPaymentDto>> CheckAsync(int paymentId, CancellationToken ct = default)
        {
            var p = await _db.Set<TerminalPayment>().FirstOrDefaultAsync(x => x.Id == paymentId, ct);
            if (p is null) return Fail("NotFound", "Card payment not found.");
            var terminal = await _db.Set<CardTerminal>().AsNoTracking().FirstAsync(t => t.Id == p.TerminalId, ct);
            if (p.Status == TerminalPaymentStatuses.Pending)
            {
                var lifetime = terminal.Provider == CardTerminalProviders.Manual ? ManualLifetime : AutoLifetime;
                var r = await Provider(terminal.Provider).GetStatusAsync(terminal, p, ct);
                p.LastCheckedOn = DateTime.UtcNow;
                if (r.Status == TerminalPaymentStatuses.Pending && DateTime.UtcNow - p.CreatedOn > lifetime)
                    r = r with { Status = TerminalPaymentStatuses.Expired, Message = "No answer from the terminal in time." };
                await ApplyResultAsync(p, r, ct);
            }
            else if (p.Status == TerminalPaymentStatuses.Approved)
            {
                await RecordTenderAndCloseAsync(p, ct); // idempotent retry
            }
            return new(true, null, p.Status, await ToDtoAsync(p.Id, ct));
        }

        public async Task<BaseResponse<TerminalPaymentDto>> ConfirmManualAsync(int paymentId, TerminalManualConfirmDto dto, CancellationToken ct = default)
        {
            var p = await _db.Set<TerminalPayment>().FirstOrDefaultAsync(x => x.Id == paymentId, ct);
            if (p is null) return Fail("NotFound", "Card payment not found.");
            var terminal = await _db.Set<CardTerminal>().AsNoTracking().FirstAsync(t => t.Id == p.TerminalId, ct);
            if (terminal.Provider != CardTerminalProviders.Manual) return Fail("Validation", "Only manual terminals take a typed approval code.");
            if (p.Status != TerminalPaymentStatuses.Pending) return Fail("Closed", $"This payment is already {p.Status}.");
            var code = (dto.ApprovalCode ?? "").Trim().ToUpperInvariant();
            if (code.Length is < 2 or > 20 || !code.All(char.IsLetterOrDigit)) return Fail("Validation", "Approval code must be 2-20 letters or digits.");
            string? last4 = null;
            if (!string.IsNullOrWhiteSpace(dto.CardLast4))
            {
                last4 = dto.CardLast4.Trim();
                if (last4.Length != 4 || !last4.All(char.IsDigit)) return Fail("Validation", "Last 4 digits must be exactly 4 digits.");
            }
            await ApplyResultAsync(p, new TerminalProviderResult(TerminalPaymentStatuses.Approved, code, last4,
                string.IsNullOrWhiteSpace(dto.CardScheme) ? null : dto.CardScheme.Trim().ToUpperInvariant(), Message: $"Approved, entered by {_user.UserName}."), ct);
            return new(true, null, "Card payment recorded.", await ToDtoAsync(p.Id, ct));
        }

        public async Task<BaseResponse<TerminalPaymentDto>> CancelAsync(int paymentId, CancellationToken ct = default)
        {
            var p = await _db.Set<TerminalPayment>().FirstOrDefaultAsync(x => x.Id == paymentId, ct);
            if (p is null) return Fail("NotFound", "Card payment not found.");
            if (p.Status != TerminalPaymentStatuses.Pending) return Fail("Closed", $"This payment is already {p.Status}.");
            var terminal = await _db.Set<CardTerminal>().AsNoTracking().FirstAsync(t => t.Id == p.TerminalId, ct);
            await Provider(terminal.Provider).CancelAsync(terminal, p, ct);
            await ApplyResultAsync(p, new TerminalProviderResult(TerminalPaymentStatuses.Cancelled, Message: $"Cancelled by {_user.UserName}."), ct);
            return new(true, null, "Card payment cancelled.", await ToDtoAsync(p.Id, ct));
        }

        public async Task<BaseResponse<TerminalPaymentDto>> CallbackAsync(string? signature, string rawBody, TerminalCallbackDto dto, CancellationToken ct = default)
        {
            var secret = await _settings.GetRawAsync(CallbackSecretKey, ct);
            if (string.IsNullOrWhiteSpace(secret)) return Fail("NotConfigured", $"Set {CallbackSecretKey} to accept terminal callbacks.");
            var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim()), Encoding.UTF8.GetBytes(rawBody ?? ""))).ToLowerInvariant();
            var given = (signature ?? "").Trim().ToLowerInvariant();
            if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
                return Fail("Unauthorized", "Invalid signature.");
            var p = await _db.Set<TerminalPayment>().FirstOrDefaultAsync(x => x.ExternalId == dto.Reference, ct);
            if (p is null) return Fail("NotFound", "Unknown payment reference.");
            if (p.Status == TerminalPaymentStatuses.Pending)
                await ApplyResultAsync(p, new TerminalProviderResult(CloudHttpTerminalProvider.MapStatus(dto.Status), dto.ApprovalCode,
                    CloudHttpTerminalProvider.Last4(dto.Last4), dto.Scheme, dto.Rrn, dto.Message, rawBody), ct);
            return new(true, null, p.Status, await ToDtoAsync(p.Id, ct));
        }

        private async Task ApplyResultAsync(TerminalPayment p, TerminalProviderResult r, CancellationToken ct)
        {
            p.Status = r.Status;
            if (r.Message is not null) p.Message = r.Message.Length > 300 ? r.Message[..300] : r.Message;
            if (r.Raw is not null) p.Raw = r.Raw;
            if (r.Status != TerminalPaymentStatuses.Pending)
            {
                p.CompletedOn = DateTime.UtcNow;
                p.ApprovalCode = Trunc(r.ApprovalCode, 20); p.CardLast4 = r.Last4; p.CardScheme = Trunc(r.Scheme, 20); p.Rrn = Trunc(r.Rrn, 40);
            }
            await _db.SaveChangesAsync(ct);
            if (p.Status == TerminalPaymentStatuses.Approved) await RecordTenderAndCloseAsync(p, ct);
        }

        private static string? Trunc(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;

        private async Task RecordTenderAndCloseAsync(TerminalPayment p, CancellationToken ct)
        {
            var reference = ReferencePrefix + p.ExternalId;
            var summary = await _tenders.SummaryAsync(p.TransactionId, ct);
            var already = summary.Success && summary.Data is not null && summary.Data.Tenders.Any(t => t.Reference == reference);
            if (!already)
            {
                var add = await _tenders.AddAsync(new AddTendersDto(p.TransactionId,
                    new List<TenderLineDto> { new(p.Currency, p.Amount, TenderMethod, reference) }), ct);
                if (!add.Success) { _log.LogError("Card payment {Id}: tender could not be recorded: {Msg}", p.Id, add.Message); return; }
                summary = add;
            }
            if ((summary.Data?.RemainingInBase ?? 0m) > 0) return;
            var status = await _db.Set<TransactionRecord>().AsNoTracking().Where(t => t.Id == p.TransactionId).Select(t => t.StatusId).FirstOrDefaultAsync(ct);
            if (status != OpenInvoiceStatus) return;
            var close = await _transactions.CloseOpenInvoice(p.TransactionId, _user.UserName ?? "card-terminal", ct);
            if (!close.Success) _log.LogWarning("Invoice {Tx} could not be closed after card payment {Id}: {Msg}", p.TransactionId, p.Id, close.Message);
        }

        private async Task ExpireStaleAsync(int terminalId, CancellationToken ct)
        {
            var stale = await (from p in _db.Set<TerminalPayment>()
                               join t in _db.Set<CardTerminal>() on p.TerminalId equals t.Id
                               where p.TerminalId == terminalId && p.Status == TerminalPaymentStatuses.Pending
                               select new { p, t.Provider }).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var s in stale.Where(s => now - s.p.CreatedOn > (s.Provider == CardTerminalProviders.Manual ? ManualLifetime : AutoLifetime)))
            {
                s.p.Status = TerminalPaymentStatuses.Expired; s.p.CompletedOn = now; s.p.Message = "No answer from the terminal in time.";
            }
            await _db.SaveChangesAsync(ct);
        }

        private async Task<TerminalPaymentDto> ToDtoAsync(int id, CancellationToken ct)
            => (await Query(_db.Set<TerminalPayment>().AsNoTracking().Where(x => x.Id == id)).ToListAsync(ct)).First();

        /// <summary>Filters go on the source rows: EF cannot translate a Where on the projected record.</summary>
        private IQueryable<TerminalPaymentDto> Query(IQueryable<TerminalPayment> source)
            => from p in source
               join t in _db.Set<CardTerminal>().AsNoTracking() on p.TerminalId equals t.Id
               join tx in _db.Set<TransactionRecord>().AsNoTracking() on p.TransactionId equals tx.Id
               select new TerminalPaymentDto(p.Id, p.TransactionId, p.TerminalId, t.Name, t.Provider, p.Amount, p.Currency, p.ExternalId,
                   p.Status, p.ApprovalCode, p.CardLast4, p.CardScheme, p.Rrn, p.Message, p.CreatedOn, p.CreatedBy, p.CompletedOn,
                   tx.StatusId == ClosedInvoiceStatus);

        public async Task<TerminalReportDto> ReportAsync(DateTime? from, DateTime? to, int? terminalId, CancellationToken ct = default)
        {
            var f = (from ?? DateTime.UtcNow.Date).ToUniversalTime();
            var t = (to ?? f.AddDays(1)).ToUniversalTime();
            var src = _db.Set<TerminalPayment>().AsNoTracking().Where(p => p.CreatedOn >= f && p.CreatedOn < t);
            if (terminalId is int tid) src = src.Where(p => p.TerminalId == tid);
            var list = await Query(src.OrderByDescending(p => p.Id).Take(1000)).ToListAsync(ct);
            list = list.OrderByDescending(p => p.Id).ToList();
            var approved = list.Where(p => p.Status == TerminalPaymentStatuses.Approved).ToList();
            return new(f, t, approved.Count, approved.Sum(p => p.Amount),
                list.Count(p => p.Status == TerminalPaymentStatuses.Declined), list.Count(p => p.Status == TerminalPaymentStatuses.Cancelled),
                list.GroupBy(p => new { p.TerminalId, p.TerminalName }).Select(g => new TerminalTotalDto(g.Key.TerminalId, g.Key.TerminalName,
                    g.Count(p => p.Status == TerminalPaymentStatuses.Approved), g.Where(p => p.Status == TerminalPaymentStatuses.Approved).Sum(p => p.Amount),
                    g.Count(p => p.Status == TerminalPaymentStatuses.Declined))).ToList(),
                approved.GroupBy(p => p.CardScheme ?? "Unknown").Select(g => new TerminalSchemeTotalDto(g.Key, g.Count(), g.Sum(p => p.Amount))).OrderByDescending(s => s.Amount).ToList(),
                list);
        }

        private static BaseResponse<TerminalPaymentDto> Fail(string e, string m) => new(false, e, m);
    }
}
