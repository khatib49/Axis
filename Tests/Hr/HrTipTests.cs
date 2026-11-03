using Application.DTOs;
using Application.Services.Hr;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Hr
{
    public class HrTipTests : IDisposable
    {
        private readonly ApplicationDbContext _db = HrTestDb.New();
        private static readonly DateOnly Day = new(2026, 11, 3);

        public HrTipTests()
        {
            _db.AddRange(
                new Employee { Id = 1, FullName = "A", IsActive = true, HiredOn = Day },
                new Employee { Id = 2, FullName = "B", IsActive = true, HiredOn = Day },
                new Employee { Id = 3, FullName = "C", IsActive = true, HiredOn = Day });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private HrTipService Svc() => new(_db, new FakeUser(), NullLogger<HrTipService>.Instance);

        [Fact]
        public void Distribute_rounds_to_cents_and_puts_the_remainder_on_the_largest_share()
        {
            // 100 / 3 = 33.33 each → 99.99; the extra cent goes to the first (all tied).
            var eq = HrCalculations.Distribute(100m, new[] { (1, 1m), (2, 1m), (3, 1m) });
            eq.Sum(x => x.Amount).Should().Be(100m);
            eq.Select(x => x.Amount).Should().BeEquivalentTo(new[] { 33.34m, 33.33m, 33.33m });
            eq[0].Amount.Should().Be(33.34m);

            // By points 5/3/1 of 50: 27.78 / 16.67 / 5.56 = 50.01 → remainder -0.01 on the largest.
            var pts = HrCalculations.Distribute(50m, new[] { (1, 5m), (2, 3m), (3, 1m) });
            pts.Sum(x => x.Amount).Should().Be(50m);
            pts.Single(x => x.EmployeeId == 1).Amount.Should().Be(27.77m);
            pts.Single(x => x.EmployeeId == 2).Amount.Should().Be(16.67m);
            pts.Single(x => x.EmployeeId == 3).Amount.Should().Be(5.56m);

            // All-zero weights fall back to an equal split.
            HrCalculations.Distribute(10m, new[] { (1, 0m), (2, 0m) }).Select(x => x.Amount).Should().AllBeEquivalentTo(5m);
        }

        [Fact]
        public async Task ByHours_weights_by_worked_minutes_on_that_date()
        {
            var start = DateTime.SpecifyKind(Day.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Utc);
            _db.AddRange(
                new AttendanceRecord { Id = 1, EmployeeId = 1, ClockIn = start, ClockOut = start.AddHours(6), WorkedMinutes = 360 },
                new AttendanceRecord { Id = 2, EmployeeId = 2, ClockIn = start, ClockOut = start.AddHours(2), WorkedMinutes = 120 },
                new AttendanceRecord { Id = 3, EmployeeId = 3, ClockIn = start.AddDays(-1), ClockOut = start.AddDays(-1).AddHours(8), WorkedMinutes = 480 }, // other day: ignored
                new AttendanceRecord { Id = 4, EmployeeId = 1, ClockIn = start.AddHours(7), ClockOut = null, WorkedMinutes = null }); // still open: ignored
            _db.SaveChanges();

            var r = await Svc().CreateAsync(new HrTipPoolCreateDto(Day, 90m, "ByHours", null,
                new[] { new HrTipParticipantDto(1), new HrTipParticipantDto(2), new HrTipParticipantDto(3) }));
            r.Success.Should().BeTrue(r.Message);
            var a = r.Data!.Allocations.ToDictionary(x => x.EmployeeId);
            a[1].Hours.Should().Be(6m); a[2].Hours.Should().Be(2m); a[3].Hours.Should().Be(0m);
            a[1].Amount.Should().Be(67.50m);
            a[2].Amount.Should().Be(22.50m);
            a[3].Amount.Should().Be(0m);
            r.Data.Allocations.Sum(x => x.Amount).Should().Be(90m);
        }

        [Fact]
        public async Task ByPoints_distributes_by_given_points_and_distribute_freezes_the_pool()
        {
            var svc = Svc();
            var r = await svc.CreateAsync(new HrTipPoolCreateDto(Day, 50m, "ByPoints", null,
                new[] { new HrTipParticipantDto(1, 5), new HrTipParticipantDto(2, 3), new HrTipParticipantDto(3, 1) }));
            r.Success.Should().BeTrue(r.Message);
            var a = r.Data!.Allocations.ToDictionary(x => x.EmployeeId);
            a[1].Amount.Should().Be(27.77m); a[2].Amount.Should().Be(16.67m); a[3].Amount.Should().Be(5.56m);

            var d = await svc.DistributeAsync(r.Data.Id);
            d.Success.Should().BeTrue();
            d.Data!.Status.Should().Be("Distributed");

            (await svc.UpdateAsync(r.Data.Id, new HrTipPoolUpdateDto(TotalAmount: 999))).Success.Should().BeFalse("frozen");
            (await svc.DeleteAsync(r.Data.Id)).Should().BeFalse("frozen");
            (await svc.DistributeAsync(r.Data.Id)).Success.Should().BeFalse("already distributed");

            var rep = await svc.ReportAsync(Day, Day);
            rep.Total.Should().Be(50m);
            rep.ByEmployee.Single(x => x.EmployeeId == 1).Amount.Should().Be(27.77m);
        }

        [Fact]
        public async Task Invalid_method_or_unknown_employee_is_refused()
        {
            var svc = Svc();
            (await svc.CreateAsync(new HrTipPoolCreateDto(Day, 10m, "Random"))).Success.Should().BeFalse();
            (await svc.CreateAsync(new HrTipPoolCreateDto(Day, 10m, "Equal", null, new[] { new HrTipParticipantDto(99) }))).Success.Should().BeFalse();
            (await svc.CreateAsync(new HrTipPoolCreateDto(Day, -1m, "Equal"))).Success.Should().BeFalse();
        }
    }
}
