using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Floor
{
    public class ReservationAvailabilityTests : FloorTestBase
    {
        private static readonly DateTime T0 = new(2026, 12, 1, 18, 0, 0, DateTimeKind.Utc);

        private ReservationUpsertDto Table1(DateTime start, DateTime end, bool confirm = true)
            => new("Ann", "70123456", Reservation.ResourceTable, start, end, 2, TableId: 1, ConfirmNow: confirm);

        [Fact]
        public async Task Overlapping_confirmed_reservation_on_the_same_table_blocks()
        {
            (await Reservations.CreateAsync(Table1(T0, T0.AddHours(2)))).Success.Should().BeTrue();

            var overlap = await Reservations.CheckAvailabilityAsync(new AvailabilityQueryDto(Reservation.ResourceTable, T0.AddHours(1), T0.AddHours(3), TableId: 1));
            overlap.Available.Should().BeFalse();
            overlap.Conflicts.Should().HaveCount(1);

            var second = await Reservations.CreateAsync(Table1(T0.AddMinutes(30), T0.AddHours(1)));
            second.Success.Should().BeFalse();
            second.Message.Should().Contain("already booked");
        }

        [Fact]
        public async Task Adjacent_windows_other_tables_and_pending_reservations_do_not_block()
        {
            (await Reservations.CreateAsync(Table1(T0, T0.AddHours(2)))).Success.Should().BeTrue();

            // back-to-back: ends exactly when the next starts
            (await Reservations.CheckAvailabilityAsync(new AvailabilityQueryDto(Reservation.ResourceTable, T0.AddHours(2), T0.AddHours(4), TableId: 1)))
                .Available.Should().BeTrue();
            // another table
            (await Reservations.CheckAvailabilityAsync(new AvailabilityQueryDto(Reservation.ResourceTable, T0, T0.AddHours(2), TableId: 2)))
                .Available.Should().BeTrue();
            // a room with the same id space is a different resource
            (await Reservations.CheckAvailabilityAsync(new AvailabilityQueryDto(Reservation.ResourceRoom, T0, T0.AddHours(2), RoomId: 1)))
                .Available.Should().BeTrue();

            // a Pending request does not block, but it cannot be confirmed while the slot is taken
            var pending = await Reservations.CreateAsync(Table1(T0, T0.AddHours(1), confirm: false));
            pending.Success.Should().BeTrue();
            pending.Data!.Status.Should().Be(Reservation.StatusPending);
            (await Reservations.CheckAvailabilityAsync(new AvailabilityQueryDto(Reservation.ResourceTable, T0.AddHours(3), T0.AddHours(4), TableId: 1)))
                .Available.Should().BeTrue();
            var confirm = await Reservations.ConfirmAsync(pending.Data.Id);
            confirm.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Cancelled_reservation_frees_the_slot()
        {
            var r = await Reservations.CreateAsync(Table1(T0, T0.AddHours(2)));
            (await Reservations.CancelAsync(r.Data!.Id)).Success.Should().BeTrue();
            (await Reservations.CheckAvailabilityAsync(new AvailabilityQueryDto(Reservation.ResourceTable, T0, T0.AddHours(2), TableId: 1)))
                .Available.Should().BeTrue();
        }

        [Fact]
        public async Task Validation_rejects_bad_windows_and_unknown_resources()
        {
            (await Reservations.CreateAsync(Table1(T0.AddHours(2), T0))).Success.Should().BeFalse("end before start");
            (await Reservations.CreateAsync(new ReservationUpsertDto("Ann", "70123456", "Table", T0, T0.AddHours(1), 2, TableId: 99, ConfirmNow: true)))
                .Success.Should().BeFalse("unknown table");
            (await Reservations.CreateAsync(new ReservationUpsertDto("Ann", "70123456", "Table", T0, T0.AddHours(1), 2, TableId: 3)))
                .Success.Should().BeFalse("inactive table");
            (await Reservations.CreateAsync(new ReservationUpsertDto("", "70123456", "Table", T0, T0.AddHours(1), 2, TableId: 1)))
                .Success.Should().BeFalse("name required");
            (await Reservations.CreateAsync(new ReservationUpsertDto("Ann", "70123456", "Spaceship", T0, T0.AddHours(1), 2)))
                .Success.Should().BeFalse("unknown resource type");
        }

        [Fact]
        public async Task Public_request_is_pending_online_and_limited_to_three_pending_per_phone()
        {
            var start = DateTime.UtcNow.AddDays(1);
            for (var i = 0; i < 3; i++)
            {
                var r = await Reservations.PublicRequestAsync(new PublicReservationRequestDto("Bob", "71000000", "Table", start.AddHours(i * 3), start.AddHours(i * 3 + 2), 4));
                r.Success.Should().BeTrue(r.Message);
                r.Data!.Status.Should().Be(Reservation.StatusPending);
                r.Data.Source.Should().Be("Online");
            }
            var fourth = await Reservations.PublicRequestAsync(new PublicReservationRequestDto("Bob", "71000000", "Table", start.AddDays(2), start.AddDays(2).AddHours(1), 4));
            fourth.Success.Should().BeFalse();
            fourth.Message.Should().Contain("pending");

            var past = await Reservations.PublicRequestAsync(new PublicReservationRequestDto("Zed", "72000000", "Table", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 2));
            past.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Seating_a_table_reservation_can_open_a_table_session()
        {
            var r = await Reservations.CreateAsync(Table1(T0, T0.AddHours(2)));
            var seated = await Reservations.SeatAsync(r.Data!.Id, new SeatReservationRequestDto(OpenTableSession: true, GuestCount: 3));
            seated.Success.Should().BeTrue(seated.Message);
            seated.Data!.Status.Should().Be(Reservation.StatusSeated);
            seated.Data.SeatedTransactionId.Should().NotBeNull();

            var session = await Sessions.GetByTransactionAsync(seated.Data.SeatedTransactionId!.Value);
            session.Should().NotBeNull();
            session!.TableId.Should().Be(1);
            session.GuestCount.Should().Be(3);
        }
    }
}
