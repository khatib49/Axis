using Application.DTOs;
using Application.Services.Hr;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Hr
{
    public class HrPinAndClockTests : IDisposable
    {
        private readonly ApplicationDbContext _db = HrTestDb.New();
        private DateTime _now = new(2026, 11, 3, 8, 5, 0, DateTimeKind.Utc);

        private HrAttendanceService Svc() => new(_db, new FakeUser(), NullLogger<HrAttendanceService>.Instance, () => _now);

        private Employee Seed(int id, string name, string pin, bool active = true)
        {
            var salt = HrPinHasher.NewSalt();
            var e = new Employee { Id = id, FullName = name, PinSalt = salt, PinHash = HrPinHasher.Hash(pin, salt), IsActive = active, HiredOn = new DateOnly(2026, 1, 1) };
            _db.Add(e);
            _db.SaveChanges();
            return e;
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Hash_is_salted_and_verify_only_accepts_the_right_pin()
        {
            var s1 = HrPinHasher.NewSalt();
            var s2 = HrPinHasher.NewSalt();
            var h1 = HrPinHasher.Hash("1234", s1);
            HrPinHasher.Hash("1234", s2).Should().NotBe(h1, "different salts give different hashes");
            h1.Should().HaveLength(64, "SHA-256 hex");
            HrPinHasher.Verify("1234", h1, s1).Should().BeTrue();
            HrPinHasher.Verify("1235", h1, s1).Should().BeFalse();
            HrPinHasher.Verify("1234", h1, s2).Should().BeFalse();
            HrPinHasher.Verify(null, h1, s1).Should().BeFalse();
        }

        [Theory]
        [InlineData("1234", true)]
        [InlineData("123456", true)]
        [InlineData("123", false)]
        [InlineData("1234567", false)]
        [InlineData("12a4", false)]
        [InlineData("", false)]
        public void Pin_must_be_4_to_6_digits(string pin, bool ok) => HrPinHasher.IsValidPin(pin).Should().Be(ok);

        [Fact]
        public async Task Clock_toggles_in_then_out_and_computes_worked_minutes()
        {
            Seed(1, "Nour", "2468");
            var svc = Svc();

            var r1 = await svc.ClockAsync(new HrClockRequestDto("2468", "till-1"));
            r1.Success.Should().BeTrue();
            r1.Action.Should().Be("ClockIn");
            r1.EmployeeName.Should().Be("Nour");

            _now = _now.AddHours(7).AddMinutes(30);
            var r2 = await svc.ClockAsync(new HrClockRequestDto("2468", "till-1"));
            r2.Action.Should().Be("ClockOut");
            r2.WorkedMinutes.Should().Be(450);

            var recs = _db.Set<AttendanceRecord>().ToList();
            recs.Should().HaveCount(1);
            recs[0].ClockOut.Should().NotBeNull();
            recs[0].Source.Should().Be(AttendanceRecord.SourcePin);

            // Next tap starts a fresh record.
            _now = _now.AddMinutes(10);
            (await svc.ClockAsync(new HrClockRequestDto("2468"))).Action.Should().Be("ClockIn");
            _db.Set<AttendanceRecord>().Count().Should().Be(2);
        }

        [Fact]
        public async Task Unknown_or_inactive_pins_are_rejected_without_creating_records()
        {
            Seed(1, "Nour", "2468");
            Seed(2, "Gone", "1357", active: false);
            var svc = Svc();
            (await svc.ClockAsync(new HrClockRequestDto("9999"))).Success.Should().BeFalse();
            (await svc.ClockAsync(new HrClockRequestDto("1357"))).Success.Should().BeFalse();
            (await svc.ClockAsync(new HrClockRequestDto("12"))).Success.Should().BeFalse();
            _db.Set<AttendanceRecord>().Should().BeEmpty();
        }

        [Fact]
        public async Task Clock_in_links_the_nearest_shift_of_the_day_within_two_hours()
        {
            Seed(1, "Nour", "2468");
            var today = DateOnly.FromDateTime(_now);
            _db.AddRange(
                new ScheduledShift { Id = 10, EmployeeId = 1, Date = today, StartOn = _now.AddHours(-6), EndOn = _now.AddHours(2) },   // too far
                new ScheduledShift { Id = 11, EmployeeId = 1, Date = today, StartOn = _now.AddMinutes(-5), EndOn = _now.AddHours(8) }, // nearest
                new ScheduledShift { Id = 12, EmployeeId = 1, Date = today, StartOn = _now.AddMinutes(90), EndOn = _now.AddHours(9) });
            _db.SaveChanges();

            await Svc().ClockAsync(new HrClockRequestDto("2468"));
            _db.Set<AttendanceRecord>().Single().ScheduledShiftId.Should().Be(11);
        }

        [Fact]
        public void Late_minutes_is_zero_when_early_and_floored_minutes_when_late()
        {
            var start = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
            HrCalculations.LateMinutes(start, start.AddMinutes(-10)).Should().Be(0);
            HrCalculations.LateMinutes(start, start).Should().Be(0);
            HrCalculations.LateMinutes(start, start.AddMinutes(12).AddSeconds(40)).Should().Be(12);
        }

        [Fact]
        public async Task Daily_summary_reports_late_minutes_against_the_scheduled_start()
        {
            Seed(1, "Nour", "2468");
            var today = DateOnly.FromDateTime(_now);
            var start = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
            _db.Add(new ScheduledShift { Id = 1, EmployeeId = 1, Date = today, StartOn = start, EndOn = start.AddHours(8) });
            _db.Add(new AttendanceRecord { Id = 1, EmployeeId = 1, ClockIn = start.AddMinutes(17), ClockOut = start.AddHours(8), WorkedMinutes = 463 });
            _db.SaveChanges();

            var rows = await Svc().DailySummaryAsync(today);
            rows.Should().ContainSingle();
            rows[0].LateMinutes.Should().Be(17);
            rows[0].WorkedMinutes.Should().Be(463);
            rows[0].Status.Should().Be("Present");
        }
    }
}
