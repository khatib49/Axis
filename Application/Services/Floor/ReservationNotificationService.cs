using System.Globalization;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Floor
{
    public class ReservationNotificationService : IReservationNotificationService
    {
        public const string ReminderTemplateKey = "Reservations.WhatsAppReminderTemplate";
        public const string ConfirmTemplateKey = "Reservations.WhatsAppConfirmTemplate";
        private const string VenueTimeZoneId = "Asia/Beirut";

        private readonly ApplicationDbContext _db;
        private readonly IWhatsAppService _whatsApp;
        private readonly IIntegrationSettingsService _settings;
        private readonly ILogger<ReservationNotificationService> _logger;

        public ReservationNotificationService(ApplicationDbContext db, IWhatsAppService whatsApp,
            IIntegrationSettingsService settings, ILogger<ReservationNotificationService> logger)
        {
            _db = db;
            _whatsApp = whatsApp;
            _settings = settings;
            _logger = logger;
        }

        public static TimeZoneInfo VenueTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(VenueTimeZoneId); }
            catch (Exception) { return TimeZoneInfo.Utc; }
        }

        // ── Manual / single sends ────────────────────────────────────
        public async Task<BaseResponse<ReservationNotificationResultDto>> SendReminderAsync(int reservationId, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == reservationId, ct);
            if (r is null) return Fail(reservationId, "Reminder", "Reservation not found.");
            if (r.Status is not (Reservation.StatusPending or Reservation.StatusConfirmed))
                return Fail(reservationId, "Reminder", $"Only a Pending or Confirmed reservation can be reminded (current: {r.Status}).");
            if (string.IsNullOrWhiteSpace(r.Phone)) return Fail(reservationId, "Reminder", "Reservation has no phone number.");

            var template = await _settings.GetRawAsync(ReminderTemplateKey, ct);
            if (string.IsNullOrWhiteSpace(template))
                return Fail(reservationId, "Reminder", $"WhatsApp reminder template is not configured ({ReminderTemplateKey}).");

            var (ok, detail) = await SendAsync(r, template, ct);
            if (!ok) return Fail(reservationId, "Reminder", detail ?? "WhatsApp send failed.");
            r.ReminderSentOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<ReservationNotificationResultDto>(true, null, "Reminder sent",
                new ReservationNotificationResultDto(r.Id, "Reminder", true, r.ReminderSentOn, null));
        }

        public async Task<BaseResponse<ReservationNotificationResultDto>> SendConfirmationAsync(int reservationId, CancellationToken ct = default)
        {
            var r = await _db.Set<Reservation>().FirstOrDefaultAsync(x => x.Id == reservationId, ct);
            if (r is null) return Fail(reservationId, "Confirmation", "Reservation not found.");
            if (string.IsNullOrWhiteSpace(r.Phone)) return Fail(reservationId, "Confirmation", "Reservation has no phone number.");

            var template = await _settings.GetRawAsync(ConfirmTemplateKey, ct);
            if (string.IsNullOrWhiteSpace(template))
                return Fail(reservationId, "Confirmation", $"WhatsApp confirmation template is not configured ({ConfirmTemplateKey}).");

            var (ok, detail) = await SendAsync(r, template, ct);
            if (!ok) return Fail(reservationId, "Confirmation", detail ?? "WhatsApp send failed.");
            r.ConfirmationSentOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<ReservationNotificationResultDto>(true, null, "Confirmation sent",
                new ReservationNotificationResultDto(r.Id, "Confirmation", true, r.ConfirmationSentOn, null));
        }

        // ── Daily job body ───────────────────────────────────────────
        public async Task<ReminderRunResultDto> SendTomorrowRemindersAsync(DateTime? nowUtc = null, CancellationToken ct = default)
        {
            var tz = VenueTimeZone();
            var now = nowUtc ?? DateTime.UtcNow;
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), tz).Date;
            var startLocal = localToday.AddDays(1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal.AddDays(1), DateTimeKind.Unspecified), tz);

            var template = await _settings.GetRawAsync(ReminderTemplateKey, ct);
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogInformation("Reservation reminders skipped: IntegrationSettings key {Key} is not configured.", ReminderTemplateKey);
                return new ReminderRunResultDto(startUtc, endUtc, false, 0, 0, 0, 0);
            }

            var candidates = await _db.Set<Reservation>()
                .Where(r => r.Status == Reservation.StatusConfirmed && r.ReminderSentOn == null && r.StartOn >= startUtc && r.StartOn < endUtc)
                .OrderBy(r => r.StartOn).ToListAsync(ct);

            int sent = 0, failed = 0, noPhone = 0;
            foreach (var r in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(r.Phone)) { noPhone++; continue; }
                var (ok, detail) = await SendAsync(r, template, ct);
                if (ok)
                {
                    r.ReminderSentOn = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    sent++;
                }
                else
                {
                    failed++;
                    _logger.LogWarning("Reservation #{Id} reminder failed: {Detail}", r.Id, detail);
                }
            }
            _logger.LogInformation("Reservation reminders for {Day:yyyy-MM-dd}: {Sent} sent, {Failed} failed, {NoPhone} without phone ({Candidates} candidates)",
                startLocal, sent, failed, noPhone, candidates.Count);
            return new ReminderRunResultDto(startUtc, endUtc, true, candidates.Count, sent, failed, noPhone);
        }

        // ── helpers ──────────────────────────────────────────────────
        private async Task<(bool ok, string? detail)> SendAsync(Reservation r, string template, CancellationToken ct)
        {
            var label = await ResourceLabelAsync(r, ct);
            var when = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.StartOn, DateTimeKind.Utc), VenueTimeZone())
                .ToString("ddd d MMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
            var parameters = new[] { r.CustomerName, when, label };
            try
            {
                var result = await _whatsApp.SendBlastAsync(null, new[] { new WhatsAppRecipient(r.Phone, r.CustomerName) }, template, parameters, ct);
                if (result.Sent > 0) return (true, null);
                return (false, result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "WhatsApp reported no message sent.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Missing credentials / network: never let a notification break the caller.
                return (false, ex.Message);
            }
        }

        private async Task<string> ResourceLabelAsync(Reservation r, CancellationToken ct)
        {
            switch (r.ResourceType)
            {
                case Reservation.ResourceTable when r.TableId.HasValue:
                    var t = await _db.Set<DiningTable>().AsNoTracking().Where(x => x.Id == r.TableId.Value)
                        .Select(x => new { x.Code, x.Name }).FirstOrDefaultAsync(ct);
                    return t is null ? "Table" : $"Table {t.Code} — {t.Name}";
                case Reservation.ResourceRoom when r.RoomId.HasValue:
                    var room = await _db.Set<Room>().AsNoTracking().Where(x => x.Id == r.RoomId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct);
                    return room is null ? "Room" : $"Room {room}";
                case Reservation.ResourceConsole when r.GameId.HasValue:
                    var game = await _db.Set<Game>().AsNoTracking().Where(x => x.Id == r.GameId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct);
                    return game is null ? "Console" : $"Console {game}";
                default:
                    return r.ResourceType;
            }
        }

        private static BaseResponse<ReservationNotificationResultDto> Fail(int id, string kind, string message)
            => new(false, "Validation", message, new ReservationNotificationResultDto(id, kind, false, null, message));
    }
}
