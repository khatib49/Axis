using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// WhatsApp messages for reservations (reminder the day before, confirmation on confirm).
    /// Template names come from IntegrationSettings:
    ///   "Reservations.WhatsAppReminderTemplate" and "Reservations.WhatsAppConfirmTemplate".
    /// Params sent (in order): customer name, date/time text (venue time), table/room label.
    /// </summary>
    public interface IReservationNotificationService
    {
        /// <summary>Manual reminder send (re-sends even when already reminded). Stamps ReminderSentOn on success.</summary>
        Task<BaseResponse<ReservationNotificationResultDto>> SendReminderAsync(int reservationId, CancellationToken ct = default);
        /// <summary>Confirmation message; silently skipped (Success=false, no error log) when the template key is not configured.</summary>
        Task<BaseResponse<ReservationNotificationResultDto>> SendConfirmationAsync(int reservationId, CancellationToken ct = default);
        /// <summary>Reminders for Confirmed reservations starting tomorrow (venue date), once per reservation, phone required.</summary>
        Task<ReminderRunResultDto> SendTomorrowRemindersAsync(DateTime? nowUtc = null, CancellationToken ct = default);
    }
}
