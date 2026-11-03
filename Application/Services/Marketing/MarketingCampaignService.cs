using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Marketing
{
    /// <summary>
    /// Campaign CRUD, scheduling and sending. Sending builds the recipient list from
    /// the segment at send time, skips unsubscribed / empty addresses, sends in
    /// batches of <see cref="BatchSize"/> (with a short pause between batches),
    /// records a status per recipient and never throws.
    /// </summary>
    public class MarketingCampaignService : IMarketingCampaignService
    {
        public static readonly string[] Channels = { "Email", "Sms", "WhatsApp" };
        public static readonly string[] Statuses = { "Draft", "Scheduled", "Sending", "Sent", "Cancelled", "Failed" };
        public const int BatchSize = 50;
        /// <summary>Pause between batches (rate limiting). Tests set it to zero.</summary>
        public static TimeSpan BatchDelay { get; set; } = TimeSpan.FromSeconds(1);

        private readonly ApplicationDbContext _db;
        private readonly IMarketingSegmentService _segments;
        private readonly IMarketingUnsubscribeService _unsub;
        private readonly IEmailSender _email;
        private readonly ISmsSender _sms;
        private readonly IWhatsAppService _wa;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<MarketingCampaignService> _logger;

        public MarketingCampaignService(ApplicationDbContext db, IMarketingSegmentService segments, IMarketingUnsubscribeService unsub,
            IEmailSender email, ISmsSender sms, IWhatsAppService wa, ICurrentUserAccessor user, ILogger<MarketingCampaignService> logger)
        {
            _db = db; _segments = segments; _unsub = unsub; _email = email; _sms = sms; _wa = wa; _user = user; _logger = logger;
        }

        // ── Pure rules (unit-tested) ─────────────────────────────────────
        public static bool IsDue(MktCampaign c, DateTime nowUtc) =>
            c.Status == "Scheduled" && c.ScheduledFor.HasValue && c.ScheduledFor.Value <= nowUtc;

        private static string? Canonical(string? raw, string[] allowed) =>
            allowed.FirstOrDefault(a => string.Equals(a, raw?.Trim(), StringComparison.OrdinalIgnoreCase));

        internal static CampaignDto ToDto(MktCampaign c, string? segmentName = null) => new(c.Id, c.Name, c.Channel, c.SegmentId,
            segmentName ?? c.Segment?.Name, c.Subject, c.Body, c.WhatsAppTemplate, c.PromoCode, c.Status, c.ScheduledFor, c.SentOn,
            c.AudienceCount, c.SentCount, c.FailedCount, c.CreatedOn, c.CreatedBy, c.BranchId);

        private static CampaignRecipientDto ToDto(MktCampaignRecipient r) =>
            new(r.Id, r.CampaignId, r.UserId, r.Address, r.Status, r.Error, r.SentOn, r.IsTest);

        // ── CRUD ─────────────────────────────────────────────────────────
        public async Task<IReadOnlyList<CampaignDto>> ListAsync(string? status = null, CancellationToken ct = default)
        {
            var q = _db.Set<MktCampaign>().AsNoTracking().Include(c => c.Segment).AsQueryable();
            var st = Canonical(status, Statuses);
            if (st is not null) q = q.Where(c => c.Status == st);
            return (await q.OrderByDescending(c => c.CreatedOn).ToListAsync(ct)).Select(c => ToDto(c)).ToList();
        }

        public async Task<CampaignDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<MktCampaign>().AsNoTracking().Include(x => x.Segment).FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null ? null : ToDto(c);
        }

        private async Task<string?> ValidateAsync(string channel, int segmentId, string? body, string? subject, string? template, CancellationToken ct)
        {
            if (!await _db.Set<MktSegment>().AnyAsync(s => s.Id == segmentId, ct)) return "Segment not found.";
            if (channel == "Email" && string.IsNullOrWhiteSpace(subject)) return "Email campaigns need a subject.";
            if (channel == "WhatsApp" && string.IsNullOrWhiteSpace(template)) return "WhatsApp campaigns need a WhatsAppTemplate (Meta template name).";
            if (channel != "WhatsApp" && string.IsNullOrWhiteSpace(body)) return "Body is required.";
            return null;
        }

        public async Task<BaseResponse<CampaignDto>> CreateAsync(CampaignCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Name is required.", "Validation failed");
            var channel = Canonical(dto.Channel, Channels);
            if (channel is null) return new(false, "Channel must be Email, Sms or WhatsApp.", "Validation failed");
            var err = await ValidateAsync(channel, dto.SegmentId, dto.Body, dto.Subject, dto.WhatsAppTemplate, ct);
            if (err is not null) return new(false, err, "Validation failed");
            var e = new MktCampaign
            {
                Name = dto.Name.Trim(), Channel = channel, SegmentId = dto.SegmentId, Subject = dto.Subject?.Trim(), Body = dto.Body ?? "",
                WhatsAppTemplate = dto.WhatsAppTemplate?.Trim(), PromoCode = dto.PromoCode?.Trim(),
                Status = dto.ScheduledFor.HasValue ? "Scheduled" : "Draft", ScheduledFor = dto.ScheduledFor?.ToUniversalTime(),
                CreatedBy = _user.UserName, CreatedOn = DateTime.UtcNow, BranchId = dto.BranchId ?? _user.BranchId,
            };
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Created", await GetAsync(e.Id, ct));
        }

        public async Task<BaseResponse<CampaignDto>> UpdateAsync(int id, CampaignUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<MktCampaign>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Campaign not found.", "Not found");
            if (e.Status is not ("Draft" or "Scheduled" or "Cancelled")) return new(false, $"A {e.Status} campaign cannot be edited.", "Invalid state");
            if (dto.Name is not null && dto.Name.Trim().Length > 0) e.Name = dto.Name.Trim();
            if (dto.Channel is not null)
            {
                var c = Canonical(dto.Channel, Channels);
                if (c is null) return new(false, "Channel must be Email, Sms or WhatsApp.", "Validation failed");
                e.Channel = c;
            }
            if (dto.SegmentId.HasValue) e.SegmentId = dto.SegmentId.Value;
            if (dto.Body is not null) e.Body = dto.Body;
            if (dto.Subject is not null) e.Subject = dto.Subject.Trim();
            if (dto.WhatsAppTemplate is not null) e.WhatsAppTemplate = dto.WhatsAppTemplate.Trim();
            if (dto.PromoCode is not null) e.PromoCode = dto.PromoCode.Trim();
            if (dto.BranchId.HasValue) e.BranchId = dto.BranchId.Value == 0 ? null : dto.BranchId.Value;
            if (dto.ScheduledFor.HasValue) { e.ScheduledFor = dto.ScheduledFor.Value.ToUniversalTime(); e.Status = "Scheduled"; }
            var err = await ValidateAsync(e.Channel, e.SegmentId, e.Body, e.Subject, e.WhatsAppTemplate, ct);
            if (err is not null) return new(false, err, "Validation failed");
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Updated", await GetAsync(e.Id, ct));
        }

        public async Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<MktCampaign>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Campaign not found.", "Not found", false);
            if (e.Status == "Sending") return new(false, "Campaign is being sent.", "Invalid state", false);
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Deleted", true);
        }

        public async Task<BaseResponse<CampaignDto>> ScheduleAsync(int id, DateTime scheduledFor, CancellationToken ct = default)
        {
            var e = await _db.Set<MktCampaign>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Campaign not found.", "Not found");
            if (e.Status is not ("Draft" or "Scheduled" or "Cancelled")) return new(false, $"A {e.Status} campaign cannot be scheduled.", "Invalid state");
            e.ScheduledFor = scheduledFor.ToUniversalTime();
            e.Status = "Scheduled";
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Scheduled", await GetAsync(e.Id, ct));
        }

        public async Task<BaseResponse<CampaignDto>> CancelAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<MktCampaign>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Campaign not found.", "Not found");
            if (e.Status is not ("Draft" or "Scheduled")) return new(false, $"A {e.Status} campaign cannot be cancelled.", "Invalid state");
            e.Status = "Cancelled";
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Cancelled", await GetAsync(e.Id, ct));
        }

        // ── Reports ──────────────────────────────────────────────────────
        public async Task<BaseResponse<CampaignStatsDto>> StatsAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<MktCampaign>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return new(false, "Campaign not found.", "Not found");
            var counts = await _db.Set<MktCampaignRecipient>().AsNoTracking().Where(r => r.CampaignId == id && !r.IsTest)
                .GroupBy(r => r.Status).Select(g => new { Status = g.Key, N = g.Count() }).ToListAsync(ct);
            int Of(string s) => counts.FirstOrDefault(x => x.Status == s)?.N ?? 0;
            return new(true, null, "OK", new CampaignStatsDto(c.Id, c.Status, c.AudienceCount, Of("Pending"), Of("Sent"), Of("Failed"), Of("Skipped"), c.SentOn));
        }

        public async Task<PaginatedResponse<CampaignRecipientDto>> RecipientsAsync(int id, string? status, string? search, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 500);
            var q = _db.Set<MktCampaignRecipient>().AsNoTracking().Where(r => r.CampaignId == id);
            var st = Canonical(status, new[] { "Pending", "Sent", "Failed", "Skipped" });
            if (st is not null) q = q.Where(r => r.Status == st);
            if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim().ToLower(); q = q.Where(r => r.Address.ToLower().Contains(s)); }
            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new PaginatedResponse<CampaignRecipientDto>(total, rows.Select(ToDto).ToList(), page, pageSize);
        }

        // ── Sending ──────────────────────────────────────────────────────
        public async Task<int> SendDueAsync(DateTime nowUtc, CancellationToken ct = default)
        {
            var candidates = await _db.Set<MktCampaign>().Where(c => c.Status == "Scheduled" && c.ScheduledFor != null && c.ScheduledFor <= nowUtc)
                .Select(c => c.Id).ToListAsync(ct);
            var n = 0;
            foreach (var id in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var r = await SendNowAsync(id, ct);
                if (r.Success) n++;
            }
            return n;
        }

        public async Task<BaseResponse<CampaignSendSummaryDto>> SendNowAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<MktCampaign>().Include(x => x.Segment).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return new(false, "Campaign not found.", "Not found");
            if (c.Status is not ("Draft" or "Scheduled" or "Cancelled")) return new(false, $"A {c.Status} campaign cannot be sent.", "Invalid state");
            if (c.Segment is null) { c.Status = "Failed"; await _db.SaveChangesAsync(ct); return new(false, "Segment not found.", "Failed"); }

            c.Status = "Sending";
            await _db.SaveChangesAsync(ct);
            int sent = 0, failed = 0, skipped = 0;
            try
            {
                var audience = await _segments.BuildAudienceAsync(SegmentRules.Parse(c.Segment.RulesJson), ct);
                var blocked = await _unsub.AllAddressesAsync(ct);
                c.AudienceCount = audience.Count;

                // Build recipient rows (one per audience member), marking skips up front.
                var rows = new List<(MktCampaignRecipient Row, AudienceRowDto Member)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in audience)
                {
                    var address = c.Channel == "Email" ? m.Email : m.Phone;
                    var row = new MktCampaignRecipient { CampaignId = c.Id, UserId = m.UserId, Address = address?.Trim() ?? "", Status = "Pending" };
                    if (string.IsNullOrWhiteSpace(row.Address)) { row.Status = "Skipped"; row.Error = "No address"; row.Address = "(none)"; }
                    else if (blocked.Contains(IMarketingUnsubscribeService.Normalise(row.Address))) { row.Status = "Skipped"; row.Error = "Unsubscribed"; }
                    else if (!seen.Add(row.Address)) { row.Status = "Skipped"; row.Error = "Duplicate address"; }
                    if (row.Status == "Skipped") skipped++;
                    rows.Add((row, m));
                }
                _db.AddRange(rows.Select(r => r.Row));
                await _db.SaveChangesAsync(ct);

                var pending = rows.Where(r => r.Row.Status == "Pending").ToList();
                for (var i = 0; i < pending.Count; i += BatchSize)
                {
                    ct.ThrowIfCancellationRequested();
                    if (i > 0 && BatchDelay > TimeSpan.Zero) await Task.Delay(BatchDelay, ct);
                    foreach (var (row, m) in pending.Skip(i).Take(BatchSize))
                    {
                        var (ok, error) = await DeliverAsync(c, row.Address, m.Name, FirstName(m.Name), m.LastVisit, ct);
                        row.Status = ok ? "Sent" : "Failed";
                        row.Error = Truncate(error, 500);
                        row.SentOn = ok ? DateTime.UtcNow : null;
                        if (ok) sent++; else failed++;
                    }
                    await _db.SaveChangesAsync(ct);
                }
                c.Status = "Sent";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Campaign {Id} failed", c.Id);
                c.Status = "Failed";
            }
            c.SentOn = DateTime.UtcNow;
            c.SentCount = sent;
            c.FailedCount = failed;
            try { await _db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex) { _logger.LogError(ex, "Campaign {Id}: could not persist final status", c.Id); }
            _logger.LogInformation("Campaign {Id} ({Channel}) → {Status}: audience {A}, sent {S}, failed {F}, skipped {K}", c.Id, c.Channel, c.Status, c.AudienceCount, sent, failed, skipped);
            return new(true, null, c.Status, new CampaignSendSummaryDto(c.Id, c.Status, c.AudienceCount, sent, failed, skipped));
        }

        public async Task<BaseResponse<CampaignTestSendResultDto>> TestSendAsync(int id, string address, CancellationToken ct = default)
        {
            var c = await _db.Set<MktCampaign>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return new(false, "Campaign not found.", "Not found");
            address = (address ?? "").Trim();
            if (address.Length == 0) return new(false, "Address is required.", "Validation failed");
            if (c.Channel == "Email" && !address.Contains('@')) return new(false, "An e-mail address is required for an Email campaign.", "Validation failed");
            var row = new MktCampaignRecipient { CampaignId = c.Id, Address = address, Status = "Pending", IsTest = true };
            if (await _unsub.IsUnsubscribedAsync(address, ct)) { row.Status = "Skipped"; row.Error = "Unsubscribed"; }
            else
            {
                var (ok, error) = await DeliverAsync(c, address, "Test Customer", "Test", DateTime.UtcNow.Date, ct);
                row.Status = ok ? "Sent" : "Failed";
                row.Error = Truncate(error, 500);
                row.SentOn = ok ? DateTime.UtcNow : null;
            }
            _db.Add(row);
            await _db.SaveChangesAsync(ct);
            return new(true, null, row.Status, new CampaignTestSendResultDto(address, row.Status, row.Error));
        }

        /// <summary>Renders + sends one message over the campaign channel. Never throws.</summary>
        private async Task<(bool Ok, string? Error)> DeliverAsync(MktCampaign c, string address, string name, string firstName, DateTime? lastVisit, CancellationToken ct)
        {
            try
            {
                switch (c.Channel)
                {
                    case "Email":
                    {
                        var unsubUrl = _unsub.BuildUnsubscribeUrl(address);
                        var values = MarketingTemplate.Values(name, firstName, lastVisit, c.PromoCode, unsubUrl);
                        var isHtml = (c.Body ?? "").TrimStart().StartsWith('<');
                        var body = MarketingTemplate.Render(c.Body, values);
                        if (!MarketingTemplate.Has(c.Body, "unsubscribeUrl"))
                            body += isHtml ? $"<p style=\"font-size:12px;color:#888\"><a href=\"{unsubUrl}\">Unsubscribe</a></p>" : $"\r\n\r\nUnsubscribe: {unsubUrl}";
                        var subject = MarketingTemplate.Render(c.Subject ?? c.Name, values);
                        var r = await _email.SendAsync(new[] { address }, subject, body, isHtml, null, ct);
                        return r.Sent ? (true, null) : (false, r.Error ?? (r.Configured ? "SMTP error" : "SMTP not configured"));
                    }
                    case "Sms":
                    {
                        var text = MarketingTemplate.Render(c.Body, MarketingTemplate.Values(name, firstName, lastVisit, c.PromoCode, null));
                        var r = await _sms.SendAsync(address, text, ct);
                        return r.Sent ? (true, null) : (false, r.Error ?? (r.Configured ? "SMS error" : "SMS not configured"));
                    }
                    case "WhatsApp":
                    {
                        if (string.IsNullOrWhiteSpace(c.WhatsAppTemplate)) return (false, "WhatsApp template not set");
                        var r = await _wa.SendBlastAsync(null, new[] { new WhatsAppRecipient(address, name, new[] { firstName }) }, c.WhatsAppTemplate.Trim(), new[] { firstName }, ct);
                        return r.Sent > 0 ? (true, null) : (false, r.Errors.FirstOrDefault() ?? "WhatsApp send failed");
                    }
                    default:
                        return (false, $"Unknown channel {c.Channel}");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return (false, "Cancelled"); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Campaign {Id}: delivery to {Address} failed", c.Id, address);
                return (false, ex.Message);
            }
        }

        private static string FirstName(string name) => (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        private static string? Truncate(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
    }
}
