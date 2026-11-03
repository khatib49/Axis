using Application.DTOs;
using Application.Services.Hr;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Hr
{
    public class HrLeaveTests : IDisposable
    {
        private readonly ApplicationDbContext _db = HrTestDb.New();
        private readonly FakeUser _user = new();

        public HrLeaveTests()
        {
            _db.Add(new Employee { Id = 1, FullName = "Nour", UserId = 1, IsActive = true, HiredOn = new DateOnly(2026, 1, 1) });
            _db.Add(new Employee { Id = 2, FullName = "Karim", UserId = 2, IsActive = true, HiredOn = new DateOnly(2026, 1, 1) });
            _db.Add(new LeaveType { Id = 1, Name = "Annual", IsPaid = true, DefaultDaysPerYear = 15 });
            _db.Add(new LeaveType { Id = 2, Name = "Sick", IsPaid = true, DefaultDaysPerYear = 5 });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private HrLeaveService Svc() => new(_db, _user, NullLogger<HrLeaveService>.Instance);

        [Fact]
        public async Task Balance_is_default_days_minus_approved_days_for_the_year_only()
        {
            _db.AddRange(
                new LeaveRequest { Id = 1, EmployeeId = 1, LeaveTypeId = 1, FromDate = new(2026, 3, 2), ToDate = new(2026, 3, 6), Days = 5, Status = LeaveRequest.StatusApproved },
                new LeaveRequest { Id = 2, EmployeeId = 1, LeaveTypeId = 1, FromDate = new(2026, 7, 1), ToDate = new(2026, 7, 2), Days = 2, Status = LeaveRequest.StatusPending },
                new LeaveRequest { Id = 3, EmployeeId = 1, LeaveTypeId = 1, FromDate = new(2026, 8, 1), ToDate = new(2026, 8, 3), Days = 3, Status = LeaveRequest.StatusRejected },
                new LeaveRequest { Id = 4, EmployeeId = 1, LeaveTypeId = 1, FromDate = new(2025, 12, 20), ToDate = new(2025, 12, 24), Days = 5, Status = LeaveRequest.StatusApproved },
                new LeaveRequest { Id = 5, EmployeeId = 2, LeaveTypeId = 1, FromDate = new(2026, 2, 1), ToDate = new(2026, 2, 1), Days = 1, Status = LeaveRequest.StatusApproved });
            _db.SaveChanges();

            var b = await Svc().BalanceAsync(1, 2026);
            b.Should().NotBeNull();
            var annual = b!.Items.Single(i => i.LeaveTypeId == 1);
            annual.UsedDays.Should().Be(5);
            annual.PendingDays.Should().Be(2);
            annual.Remaining.Should().Be(10);
            b.Items.Single(i => i.LeaveTypeId == 2).Remaining.Should().Be(5);
        }

        [Fact]
        public async Task Self_request_uses_the_user_link_and_approval_consumes_balance()
        {
            _user.Roles = new[] { "cashier" }; _user.UserId = 2; // Karim
            var svc = Svc();
            var r = await svc.CreateRequestAsync(new HrLeaveRequestCreateDto(1, new(2026, 5, 4), new(2026, 5, 8), "holiday"), isAdmin: false);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.EmployeeId.Should().Be(2);
            r.Data.Days.Should().Be(5);
            r.Data.Status.Should().Be("Pending");

            // Cannot request for someone else.
            (await svc.CreateRequestAsync(new HrLeaveRequestCreateDto(1, new(2026, 6, 1), new(2026, 6, 1), null, EmployeeId: 1), isAdmin: false)).Success.Should().BeFalse();

            _user.Roles = new[] { "admin" }; _user.UserId = 1;
            var d = await Svc().DecideAsync(r.Data.Id, true, "ok");
            d.Success.Should().BeTrue();
            d.Data!.Status.Should().Be("Approved");
            d.Data.DecidedBy.Should().Be("admin@axislb.com");
            (await Svc().BalanceAsync(2, 2026))!.Items.Single(i => i.LeaveTypeId == 1).Remaining.Should().Be(10);

            // Deciding twice is refused.
            (await Svc().DecideAsync(r.Data.Id, false, null)).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Overlapping_and_inverted_ranges_are_rejected()
        {
            var svc = Svc();
            (await svc.CreateRequestAsync(new HrLeaveRequestCreateDto(1, new(2026, 5, 4), new(2026, 5, 8), null, 1), true)).Success.Should().BeTrue();
            (await svc.CreateRequestAsync(new HrLeaveRequestCreateDto(2, new(2026, 5, 8), new(2026, 5, 9), null, 1), true)).Success.Should().BeFalse("overlaps");
            (await svc.CreateRequestAsync(new HrLeaveRequestCreateDto(2, new(2026, 5, 12), new(2026, 5, 10), null, 1), true)).Success.Should().BeFalse("inverted");
            (await svc.CreateRequestAsync(new HrLeaveRequestCreateDto(2, new(2026, 5, 9), new(2026, 5, 9), null, 1), true)).Success.Should().BeTrue("adjacent is fine");
        }
    }
}
