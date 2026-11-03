using Application.Services.Floor;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Floor
{
    public class ReservationReminderTests : FloorTestBase
    {
        // 2026-12-01 12:00 UTC → 14:00 in Beirut (UTC+2 in winter); "tomorrow" is 2026-12-02 local.
        private static readonly DateTime NowUtc = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeZoneInfo Tz = ReservationNotificationService.VenueTimeZone();

        private static DateTime LocalUtc(int day, int hour)
            => TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 12, day, hour, 0, 0, DateTimeKind.Unspecified), Tz);

        private Reservation Seed(int id, string status, DateTime startUtc, string phone = "70123456", DateTime? reminded = null)
        {
            var r = new Reservation
            {
                Id = id, CustomerName = $"Guest {id}", Phone = phone, ResourceType = Reservation.ResourceTable, TableId = 1,
                StartOn = startUtc, EndOn = startUtc.AddHours(2), PartySize = 2, Status = status, CreatedBy = "seed", ReminderSentOn = reminded,
            };
            Db.Add(r);
            Db.SaveChanges();
            return r;
        }

        [Fact]
        public async Task Job_sends_only_to_confirmed_reservations_starting_tomorrow_with_a_phone_and_not_yet_reminded()
        {
            Settings.Values[ReservationNotificationService.ReminderTemplateKey] = "reservation_reminder";
            Seed(1, Reservation.StatusConfirmed, LocalUtc(2, 20));                   // tomorrow → sent
            Seed(2, Reservation.StatusConfirmed, LocalUtc(2, 0));                    // tomorrow 00:00 local (edge) → sent
            Seed(3, Reservation.StatusConfirmed, LocalUtc(1, 23));                   // today → skipped
            Seed(4, Reservation.StatusConfirmed, LocalUtc(3, 0));                    // day after → skipped
            Seed(5, Reservation.StatusPending, LocalUtc(2, 19));                     // not confirmed → skipped
            Seed(6, Reservation.StatusConfirmed, LocalUtc(2, 19), reminded: NowUtc); // already reminded → skipped
            Seed(7, Reservation.StatusConfirmed, LocalUtc(2, 19), phone: " ");       // no phone → skipped, counted

            var run = await Notifications.SendTomorrowRemindersAsync(NowUtc);

            run.TemplateConfigured.Should().BeTrue();
            run.Candidates.Should().Be(3);
            run.Sent.Should().Be(2);
            run.SkippedNoPhone.Should().Be(1);
            run.Failed.Should().Be(0);
            WhatsApp.Sent.Select(s => s.Template).Should().OnlyContain(t => t == "reservation_reminder");
            WhatsApp.Sent.Should().HaveCount(2);
            WhatsApp.Sent[0].Params.Should().HaveCount(3);
            WhatsApp.Sent[0].Params[0].Should().Be("Guest 2");
            WhatsApp.Sent[0].Params[2].Should().Be("Table T1 — Table 1");

            Db.Set<Reservation>().Find(1)!.ReminderSentOn.Should().NotBeNull();
            Db.Set<Reservation>().Find(2)!.ReminderSentOn.Should().NotBeNull();
            Db.Set<Reservation>().Find(3)!.ReminderSentOn.Should().BeNull();
            Db.Set<Reservation>().Find(5)!.ReminderSentOn.Should().BeNull();
            Db.Set<Reservation>().Find(7)!.ReminderSentOn.Should().BeNull();
        }

        [Fact]
        public async Task Job_runs_once_per_reservation_and_keeps_unsent_ones_for_the_next_run()
        {
            Settings.Values[ReservationNotificationService.ReminderTemplateKey] = "reservation_reminder";
            Seed(1, Reservation.StatusConfirmed, LocalUtc(2, 20));
            Seed(2, Reservation.StatusConfirmed, LocalUtc(2, 21));

            WhatsApp.FailNext = true; // first candidate fails, second goes through
            var first = await Notifications.SendTomorrowRemindersAsync(NowUtc);
            first.Sent.Should().Be(1);
            first.Failed.Should().Be(1);

            var second = await Notifications.SendTomorrowRemindersAsync(NowUtc.AddHours(1));
            second.Candidates.Should().Be(1, "the one that was sent is stamped and not picked again");
            second.Sent.Should().Be(1);
            WhatsApp.Sent.Should().HaveCount(2);

            (await Notifications.SendTomorrowRemindersAsync(NowUtc.AddHours(2))).Candidates.Should().Be(0);
        }

        [Fact]
        public async Task Job_is_a_no_op_when_the_template_is_not_configured()
        {
            Seed(1, Reservation.StatusConfirmed, LocalUtc(2, 20));
            var run = await Notifications.SendTomorrowRemindersAsync(NowUtc);
            run.TemplateConfigured.Should().BeFalse();
            run.Sent.Should().Be(0);
            WhatsApp.Sent.Should().BeEmpty();
            Db.Set<Reservation>().Find(1)!.ReminderSentOn.Should().BeNull();
        }

        [Fact]
        public async Task Manual_reminder_requires_template_and_phone_and_stamps_the_reservation()
        {
            Seed(1, Reservation.StatusConfirmed, LocalUtc(5, 20));
            (await Notifications.SendReminderAsync(1)).Success.Should().BeFalse("template not configured");

            Settings.Values[ReservationNotificationService.ReminderTemplateKey] = "reservation_reminder";
            var ok = await Notifications.SendReminderAsync(1);
            ok.Success.Should().BeTrue(ok.Message);
            ok.Data!.Sent.Should().BeTrue();
            Db.Set<Reservation>().Find(1)!.ReminderSentOn.Should().NotBeNull();

            Seed(2, Reservation.StatusCancelled, LocalUtc(5, 20));
            (await Notifications.SendReminderAsync(2)).Success.Should().BeFalse("cancelled reservations are not reminded");
            (await Notifications.SendReminderAsync(99)).Success.Should().BeFalse("unknown id");
        }

        [Fact]
        public async Task Confirm_sends_the_confirmation_message_only_when_its_template_is_configured()
        {
            Seed(1, Reservation.StatusPending, LocalUtc(5, 20));
            var plain = await Reservations.ConfirmAsync(1);
            plain.Success.Should().BeTrue(plain.Message);
            plain.Data!.ConfirmationSentOn.Should().BeNull();
            WhatsApp.Sent.Should().BeEmpty();

            Settings.Values[ReservationNotificationService.ConfirmTemplateKey] = "reservation_confirmed";
            Seed(2, Reservation.StatusPending, LocalUtc(6, 20));
            var withMsg = await Reservations.ConfirmAsync(2);
            withMsg.Success.Should().BeTrue(withMsg.Message);
            withMsg.Data!.ConfirmationSentOn.Should().NotBeNull();
            WhatsApp.Sent.Should().ContainSingle(s => s.Template == "reservation_confirmed" && s.Phone == "70123456");

            // A failing send never fails the confirm itself.
            WhatsApp.FailNext = true;
            Seed(3, Reservation.StatusPending, LocalUtc(7, 20));
            var failedSend = await Reservations.ConfirmAsync(3);
            failedSend.Success.Should().BeTrue();
            failedSend.Data!.Status.Should().Be(Reservation.StatusConfirmed);
            failedSend.Data.ConfirmationSentOn.Should().BeNull();
        }
    }
}
