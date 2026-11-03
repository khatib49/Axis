using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Payments2
{
    /// <summary>
    /// Whish Collect against an existing open invoice. Amount = TotalPrice minus
    /// tenders already recorded (TenderService summary). When Whish confirms the
    /// collect, a tender is recorded through ITenderService and the invoice is
    /// closed through the existing CloseOpenInvoice when nothing remains.
    /// </summary>
    public class WhishInvoicePaymentService : IWhishInvoicePaymentService
    {
        public const string BaseCurrency = "USD";
        /// <summary>Tender method used for the Whish tender (TenderMethods has no "Whish" entry; the reference carries the provider id).</summary>
        public const string TenderMethod = "Whish";
        public const string ReferencePrefix = "Whish:";
        private const int OpenInvoiceStatus = 7;
        private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _db;
        private readonly IWhishCollectClient _whish;
        private readonly ITenderService _tenders;
        private readonly ITransactionRecordService _transactions;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<WhishInvoicePaymentService> _log;

        public WhishInvoicePaymentService(
            ApplicationDbContext db, IWhishCollectClient whish, ITenderService tenders,
            ITransactionRecordService transactions, ICurrentUserAccessor user, ILogger<WhishInvoicePaymentService> log)
        {
            _db = db; _whish = whish; _tenders = tenders; _transactions = transactions; _user = user; _log = log;
        }

        public async Task<BaseResponse<WhishStartResultDto>> StartAsync(int transactionId, string source, string successUrl, string cancelUrl, string? uiReturnUrl = null, CancellationToken ct = default)
        {
            source = WhishRequestSources.All.FirstOrDefault(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase)) ?? WhishRequestSources.Till;

            if (!await _whish.IsConfiguredAsync(ct))
                return Fail<WhishStartResultDto>("NotConfigured", "Whish is not configured. Set Whish.Channel and Whish.Secret in Integrations.");

            var tx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (tx is null) return Fail<WhishStartResultDto>("NotFound", $"Invoice #{transactionId} not found.");
            if (tx.StatusId != OpenInvoiceStatus) return Fail<WhishStartResultDto>("Closed", "Only open invoices can be paid online; this one is already closed.");

            var summary = await _tenders.SummaryAsync(transactionId, ct);
            var remaining = summary.Success && summary.Data is not null ? summary.Data.RemainingInBase : Math.Round(tx.TotalPrice, 2);
            if (remaining <= 0) return Fail<WhishStartResultDto>("NothingToPay", "Nothing remains to pay on this invoice.");

            // Reuse a fresh pending request for the same amount so a page that polls / retries does not pile up collects.
            var since = DateTime.UtcNow - ReuseWindow;
            var existing = await _db.Set<WhishRequest>()
                .Where(r => r.TransactionId == transactionId && r.Status == WhishRequestStatuses.Pending && r.Amount == remaining && r.CreatedOn >= since && r.CollectUrl != null)
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            if (existing is not null)
                return Ok(ToStart(existing), "Existing Whish request reused.");

            var req = new WhishRequest
            {
                TransactionId = transactionId,
                Amount = remaining,
                Currency = BaseCurrency,
                ExternalId = $"inv-{transactionId}-{Guid.NewGuid():N}",
                Status = WhishRequestStatuses.Pending,
                Source = source,
                ReturnUrl = string.IsNullOrWhiteSpace(uiReturnUrl) ? null : uiReturnUrl.Trim(),
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName ?? "system",
                BranchId = _user.BranchId,
            };
            _db.Set<WhishRequest>().Add(req);
            await _db.SaveChangesAsync(ct);

            var ok = successUrl.Replace("{requestId}", req.Id.ToString());
            var ko = cancelUrl.Replace("{requestId}", req.Id.ToString());
            var collect = await _whish.CreateCollectAsync(req.ExternalId, req.Amount, req.Currency, $"Axis invoice #{transactionId}", ok, ko, ct);
            if (!collect.Success || string.IsNullOrWhiteSpace(collect.CollectUrl))
            {
                req.Status = WhishRequestStatuses.Failed;
                req.RawCallback = collect.Raw ?? collect.Error;
                await _db.SaveChangesAsync(ct);
                return Fail<WhishStartResultDto>("ProviderError", collect.Error ?? "Whish did not accept the request.");
            }

            req.CollectUrl = collect.CollectUrl;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Whish request {Req} created for invoice {Tx} ({Amount} {Cur}, source {Source})", req.Id, transactionId, req.Amount, req.Currency, source);
            return Ok(ToStart(req), "Whish request created.");
        }

        public async Task<BaseResponse<WhishStatusDto>> CheckAsync(int requestId, CancellationToken ct = default)
        {
            var req = await _db.Set<WhishRequest>().FirstOrDefaultAsync(r => r.Id == requestId, ct);
            if (req is null) return Fail<WhishStatusDto>("NotFound", "Whish request not found.");

            if (req.Status == WhishRequestStatuses.Pending)
            {
                var status = await _whish.GetStatusAsync(req.ExternalId, ct);
                req.LastCheckedOn = DateTime.UtcNow;
                if (status.Status == "success")
                {
                    req.Status = WhishRequestStatuses.Paid;
                    req.PaidOn = DateTime.UtcNow;
                    req.RawCallback = status.Raw;
                    await _db.SaveChangesAsync(ct);
                    await RecordTenderAndCloseAsync(req, ct);
                }
                else if (status.Status == "failed")
                {
                    req.Status = WhishRequestStatuses.Failed;
                    req.RawCallback = status.Raw;
                    await _db.SaveChangesAsync(ct);
                }
                else if (DateTime.UtcNow - req.CreatedOn > PendingLifetime)
                {
                    req.Status = WhishRequestStatuses.Expired;
                    await _db.SaveChangesAsync(ct);
                }
                else
                {
                    await _db.SaveChangesAsync(ct);
                }
            }
            else if (req.Status == WhishRequestStatuses.Paid)
            {
                // Idempotent: a paid request whose tender / close did not go through (e.g. transient failure) is retried here.
                await RecordTenderAndCloseAsync(req, ct);
            }

            var closed = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.Id == req.TransactionId).Select(t => t.StatusId).FirstOrDefaultAsync(ct) == 6;
            return Ok(new WhishStatusDto(req.Id, req.TransactionId, req.Status, req.Amount, req.Currency, closed, req.PaidOn, req.CollectUrl), req.Status);
        }

        /// <summary>Records the tender once (keyed by the provider reference) and closes the invoice when fully paid.</summary>
        private async Task RecordTenderAndCloseAsync(WhishRequest req, CancellationToken ct)
        {
            var reference = ReferencePrefix + req.ExternalId;
            var summary = await _tenders.SummaryAsync(req.TransactionId, ct);
            var already = summary.Success && summary.Data is not null && summary.Data.Tenders.Any(t => t.Reference == reference);
            if (!already)
            {
                var add = await _tenders.AddAsync(new AddTendersDto(req.TransactionId,
                    new List<TenderLineDto> { new(req.Currency, req.Amount, TenderMethod, reference) }), ct);
                if (!add.Success)
                {
                    _log.LogError("Whish request {Req}: tender could not be recorded: {Msg}", req.Id, add.Message);
                    return;
                }
                summary = add;
            }

            var remaining = summary.Data?.RemainingInBase ?? 0m;
            if (remaining > 0) return;

            var status = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.Id == req.TransactionId).Select(t => t.StatusId).FirstOrDefaultAsync(ct);
            if (status != OpenInvoiceStatus) return;

            var close = await _transactions.CloseOpenInvoice(req.TransactionId, _user.UserName ?? "whish", ct);
            if (close.Success) _log.LogInformation("Invoice {Tx} closed after Whish request {Req}", req.TransactionId, req.Id);
            else _log.LogWarning("Invoice {Tx} could not be closed after Whish request {Req}: {Msg}", req.TransactionId, req.Id, close.Message);
        }

        public async Task<string?> GetReturnUrlAsync(int requestId, CancellationToken ct = default)
            => await _db.Set<WhishRequest>().AsNoTracking().Where(r => r.Id == requestId).Select(r => r.ReturnUrl).FirstOrDefaultAsync(ct);

        public async Task<bool> ValidatePublicTokenAsync(string? token, int transactionId, CancellationToken ct = default)
        {
            var t = (token ?? string.Empty).Trim();
            if (t.Length == 0) return false;

            if (Guid.TryParse(t, out var guid))
            {
                var session = await _db.Set<SelfOrderSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Token == guid, ct);
                if (session is not null) return session.TransactionId == transactionId;
            }

            var device = await _db.Set<KioskDevice>().AsNoTracking().FirstOrDefaultAsync(d => d.Token == t, ct);
            if (device is null || !device.IsActive) return false;
            var createdBy = $"kiosk:{device.Name}";
            return await _db.Set<DeliveryOrder>().AsNoTracking()
                .AnyAsync(o => o.TransactionId == transactionId && o.ExternalSource == "kiosk" && o.CreatedBy == createdBy, ct);
        }

        private static WhishStartResultDto ToStart(WhishRequest r) => new(r.Id, r.CollectUrl!, r.CollectUrl!, r.Amount, r.Currency, r.Status);
        private static BaseResponse<T> Fail<T>(string error, string msg) => new(false, error, msg);
        private static BaseResponse<T> Ok<T>(T data, string msg) => new(true, null, msg, data);
    }
}
