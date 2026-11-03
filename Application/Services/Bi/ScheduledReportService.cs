using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Bi
{
    public class ScheduledReportService : IScheduledReportService
    {
        private static readonly string[] Frequencies = { "Daily", "Weekly", "Monthly" };
        private static readonly string[] Channels = { "Email", "WhatsApp", "Both" };
        private static readonly string[] Formats = { "Html", "Csv" };
        private const int PreviewLength = 2000;

        private readonly ApplicationDbContext _db;
        private readonly IBiReportService _reports;
        private readonly IEmailSender _email;
        private readonly IWhatsAppService _wa;
        private readonly IIntegrationSettingsService _settings;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<ScheduledReportService> _logger;

        public ScheduledReportService(ApplicationDbContext db, IBiReportService reports, IEmailSender email, IWhatsAppService wa,
            IIntegrationSettingsService settings, ICurrentUserAccessor user, ILogger<ScheduledReportService> logger)
        {
            _db = db; _reports = reports; _email = email; _wa = wa; _settings = settings; _user = user; _logger = logger;
        }

        // ── Due rule (pure, unit-tested) ─────────────────────────────────
        /// <summary>
        /// Daily: RunAtHourUtc == now hour. Weekly: also DayOfWeek matches (0 = Sunday).
        /// Monthly: also day 1. A report that already ran in this hour is never due twice.
        /// </summary>
        public static bool IsDue(ScheduledReport r, DateTime nowUtc)
        {
            if (!r.IsActive) return false;
            if (r.RunAtHourUtc != nowUtc.Hour) return false;
            var hourStart = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
            if (r.LastRunOn.HasValue && r.LastRunOn.Value >= hourStart) return false;
            switch ((r.Frequency ?? "").Trim().ToLowerInvariant())
            {
                case "daily": return true;
                case "weekly": return r.DayOfWeek.HasValue && r.DayOfWeek.Value == (int)nowUtc.DayOfWeek;
                case "monthly": return nowUtc.Day == 1;
                default: return false;
            }
        }

        internal static ScheduledReportDto ToDto(ScheduledReport r) => new(r.Id, r.Name, r.ReportType, r.Frequency, r.RunAtHourUtc, r.DayOfWeek,
            r.Recipients, r.Channel, r.Format, r.IsActive, r.LastRunOn, r.LastStatus, r.CreatedBy, r.CreatedOn, r.BranchId);

        private static ReportRunDto ToDto(ReportRun r) => new(r.Id, r.ScheduledReportId, r.RanOn, r.Status, r.Error, r.PayloadPreview, r.IsManual);

        private static string? Canonical(string? raw, string[] allowed) =>
            allowed.FirstOrDefault(a => string.Equals(a, raw?.Trim(), StringComparison.OrdinalIgnoreCase));

        // ── CRUD ─────────────────────────────────────────────────────────
        public async Task<IReadOnlyList<ScheduledReportDto>> ListAsync(CancellationToken ct = default) =>
            (await _db.Set<ScheduledReport>().AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto).ToList();

        public async Task<ScheduledReportDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<ScheduledReport>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return r is null ? null : ToDto(r);
        }

        public async Task<BaseResponse<ScheduledReportDto>> CreateAsync(ScheduledReportCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Name is required.", "Validation failed");
            var type = Canonical(dto.ReportType, BiReportService.ReportTypes);
            if (type is null) return new(false, $"ReportType must be one of {string.Join(", ", BiReportService.ReportTypes)}.", "Validation failed");
            var freq = Canonical(dto.Frequency, Frequencies);
            if (freq is null) return new(false, "Frequency must be Daily, Weekly or Monthly.", "Validation failed");
            var channel = Canonical(dto.Channel, Channels);
            if (channel is null) return new(false, "Channel must be Email, WhatsApp or Both.", "Validation failed");
            var format = Canonical(dto.Format, Formats);
            if (format is null) return new(false, "Format must be Html or Csv.", "Validation failed");
            if (dto.RunAtHourUtc is < 0 or > 23) return new(false, "RunAtHourUtc must be 0..23.", "Validation failed");
            if (freq == "Weekly" && (dto.DayOfWeek is null or < 0 or > 6)) return new(false, "Weekly reports need DayOfWeek 0 (Sunday) .. 6 (Saturday).", "Validation failed");
            if (string.IsNullOrWhiteSpace(dto.Recipients)) return new(false, "At least one recipient is required.", "Validation failed");

            var e = new ScheduledReport
            {
                Name = dto.Name.Trim(), ReportType = type, Frequency = freq, RunAtHourUtc = dto.RunAtHourUtc,
                DayOfWeek = freq == "Weekly" ? dto.DayOfWeek : null, Recipients = NormaliseRecipients(dto.Recipients),
                Channel = channel, Format = format, IsActive = dto.IsActive, CreatedBy = _user.UserName, BranchId = dto.BranchId ?? _user.BranchId,
            };
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Created", ToDto(e));
        }

        public async Task<BaseResponse<ScheduledReportDto>> UpdateAsync(int id, ScheduledReportUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<ScheduledReport>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Report not found.", "Not found");
            if (dto.Name is not null && dto.Name.Trim().Length > 0) e.Name = dto.Name.Trim();
            if (dto.ReportType is not null)
            {
                var t = Canonical(dto.ReportType, BiReportService.ReportTypes);
                if (t is null) return new(false, "Unknown report type.", "Validation failed");
                e.ReportType = t;
            }
            if (dto.Frequency is not null)
            {
                var f = Canonical(dto.Frequency, Frequencies);
                if (f is null) return new(false, "Frequency must be Daily, Weekly or Monthly.", "Validation failed");
                e.Frequency = f;
            }
            if (dto.Channel is not null)
            {
                var c = Canonical(dto.Channel, Channels);
                if (c is null) return new(false, "Channel must be Email, WhatsApp or Both.", "Validation failed");
                e.Channel = c;
            }
            if (dto.Format is not null)
            {
                var f = Canonical(dto.Format, Formats);
                if (f is null) return new(false, "Format must be Html or Csv.", "Validation failed");
                e.Format = f;
            }
            if (dto.RunAtHourUtc.HasValue)
            {
                if (dto.RunAtHourUtc is < 0 or > 23) return new(false, "RunAtHourUtc must be 0..23.", "Validation failed");
                e.RunAtHourUtc = dto.RunAtHourUtc.Value;
            }
            if (dto.DayOfWeek.HasValue) e.DayOfWeek = dto.DayOfWeek.Value is >= 0 and <= 6 ? dto.DayOfWeek.Value : null;
            if (e.Frequency == "Weekly" && e.DayOfWeek is null) return new(false, "Weekly reports need DayOfWeek 0..6.", "Validation failed");
            if (e.Frequency != "Weekly") e.DayOfWeek = null;
            if (dto.Recipients is not null) e.Recipients = NormaliseRecipients(dto.Recipients);
            if (dto.IsActive.HasValue) e.IsActive = dto.IsActive.Value;
            if (dto.BranchId.HasValue) e.BranchId = dto.BranchId.Value == 0 ? null : dto.BranchId.Value;
            e.ModifiedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Updated", ToDto(e));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<ScheduledReport>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return false;
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<ReportRunDto>> GetRunsAsync(int id, int take = 50, CancellationToken ct = default) =>
            (await _db.Set<ReportRun>().AsNoTracking().Where(r => r.ScheduledReportId == id)
                .OrderByDescending(r => r.RanOn).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)).Select(ToDto).ToList();

        private static string NormaliseRecipients(string raw) =>
            string.Join(",", raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct());

        // ── Execution ────────────────────────────────────────────────────
        public async Task<BaseResponse<ReportPreviewDto>> PreviewAsync(int id, DateTime? day, CancellationToken ct = default)
        {
            var r = await _db.Set<ScheduledReport>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new(false, "Report not found.", "Not found");
            try
            {
                var payload = await _reports.BuildAsync(r.ReportType, day ?? DateTime.UtcNow, r.BranchId, ct);
                var content = _reports.Render(payload, r.Format);
                return new(true, null, "OK", new ReportPreviewDto(payload.Title, payload.ReportType, r.Format, content, payload.Rows.Count));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Preview of scheduled report {Id} failed", id);
                return new(false, ex.Message, "Failed");
            }
        }

        public async Task<BaseResponse<ReportRunDto>> RunNowAsync(int id, DateTime? day, CancellationToken ct = default)
        {
            var r = await _db.Set<ScheduledReport>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new(false, "Report not found.", "Not found");
            var run = await ExecuteAsync(r, day ?? DateTime.UtcNow, manual: true, ct);
            return new(true, null, run.Status, ToDto(run));
        }

        public async Task<int> RunDueAsync(DateTime nowUtc, CancellationToken ct = default)
        {
            var candidates = await _db.Set<ScheduledReport>().Where(r => r.IsActive && r.RunAtHourUtc == nowUtc.Hour).ToListAsync(ct);
            var due = candidates.Where(r => IsDue(r, nowUtc)).ToList();
            // Scheduled runs describe the previous (completed) business day.
            var day = BusinessDate.ToDay(nowUtc).AddDays(-1);
            foreach (var r in due)
            {
                ct.ThrowIfCancellationRequested();
                await ExecuteAsync(r, day, manual: false, ct);
            }
            return due.Count;
        }

        private async Task<ReportRun> ExecuteAsync(ScheduledReport r, DateTime day, bool manual, CancellationToken ct)
        {
            var run = new ReportRun { ScheduledReportId = r.Id, RanOn = DateTime.UtcNow, IsManual = manual };
            var errors = new List<string>();
            var statuses = new List<string>();
            try
            {
                var payload = await _reports.BuildAsync(r.ReportType, day, r.BranchId, ct);
                var content = _reports.Render(payload, r.Format);
                run.PayloadPreview = content.Length > PreviewLength ? content[..PreviewLength] : content;

                var all = r.Recipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                var emails = all.Where(x => x.Contains('@')).ToList();
                var phones = all.Where(x => !x.Contains('@')).ToList();
                var summaryLine = string.Join(" · ", payload.Summary.Select(kv => $"{kv.Key}: {kv.Value}"));

                if (r.Channel is "Email" or "Both")
                {
                    if (emails.Count == 0) statuses.Add("Email skipped: no e-mail recipients");
                    else
                    {
                        var isHtml = r.Format == "Html";
                        var body = isHtml ? content : $"{payload.Title}\r\n{summaryLine}\r\n\r\nThe report is attached as CSV.";
                        var attachment = isHtml ? null : new EmailAttachment($"{payload.ReportType}-{day:yyyyMMdd}.csv", content);
                        var res = await _email.SendAsync(emails, payload.Title, body, isHtml, attachment, ct);
                        if (!res.Configured) statuses.Add("Skipped: SMTP not configured");
                        else if (!res.Sent) { statuses.Add("Email failed"); errors.Add(res.Error ?? "SMTP error"); }
                        else statuses.Add("Email sent");
                    }
                }
                if (r.Channel is "WhatsApp" or "Both")
                {
                    if (phones.Count == 0) statuses.Add("WhatsApp skipped: no phone recipients");
                    else
                    {
                        var template = await _settings.GetRawAsync("Reports.WhatsAppTemplate", ct);
                        if (string.IsNullOrWhiteSpace(template)) statuses.Add("Skipped: WhatsApp template not configured");
                        else
                        {
                            var res = await _wa.SendBlastAsync(null, phones.Select(p => new WhatsAppRecipient(p, null)), template.Trim(),
                                new[] { payload.Title, summaryLine }, ct);
                            if (res.Sent == 0) { statuses.Add("WhatsApp failed"); errors.AddRange(res.Errors); }
                            else if (res.Failed > 0) { statuses.Add($"WhatsApp partial ({res.Sent}/{res.Sent + res.Failed})"); errors.AddRange(res.Errors); }
                            else statuses.Add("WhatsApp sent");
                        }
                    }
                }
                run.Status = statuses.Count == 0 ? "Skipped: no channel" : string.Join("; ", statuses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled report {Id} ({Type}) failed", r.Id, r.ReportType);
                run.Status = "Failed: " + Truncate(ex.Message, 280);
                errors.Add(ex.ToString());
            }
            run.Status = Truncate(run.Status, 300);
            run.Error = errors.Count == 0 ? null : string.Join("\n", errors);
            r.LastRunOn = run.RanOn;
            r.LastStatus = run.Status;
            _db.Add(run);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Scheduled report {Id} ({Type}) → {Status}", r.Id, r.ReportType, run.Status);
            return run;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
