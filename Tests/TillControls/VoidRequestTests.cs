using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Tests.TillControls
{
    public class VoidRequestTests : IDisposable
    {
        private readonly TillFixture _f = new();
        public void Dispose() => _f.Dispose();

        [Fact]
        public async Task Void_with_a_no_approval_reason_is_applied_immediately()
        {
            _f.AddPaidTransaction(5, 20m);
            var reason = await _f.Voids.CreateReasonAsync(new VoidReasonCreateDto("Wrong item", RequiresApproval: false));
            _f.Transactions.Setup(t => t.DeleteAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var r = await _f.Voids.CreateAsync(new VoidRequestCreateDto(5, "Void", null, reason.Data!.Id, "keyed wrong"));

            r.Success.Should().BeTrue();
            r.Data!.Status.Should().Be(VoidRequestStatuses.Approved);
            r.Data.DecisionNote.Should().Contain("Auto-approved");
            _f.Transactions.Verify(t => t.DeleteAsync(5, It.IsAny<CancellationToken>()), Times.Once);
            (await _f.Voids.ListPendingAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Void_with_an_approval_reason_waits_for_an_admin()
        {
            _f.AddPaidTransaction(6, 20m);
            var reason = await _f.Voids.CreateReasonAsync(new VoidReasonCreateDto("Customer complaint", RequiresApproval: true));
            _f.Transactions.Setup(t => t.DeleteAsync(6, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var r = await _f.Voids.CreateAsync(new VoidRequestCreateDto(6, "void", null, reason.Data!.Id, null));
            r.Data!.Status.Should().Be(VoidRequestStatuses.Pending);
            _f.Transactions.Verify(t => t.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            (await _f.Voids.ListPendingAsync()).Should().ContainSingle(v => v.Id == r.Data.Id);

            // A second pending request on the same transaction is refused.
            (await _f.Voids.CreateAsync(new VoidRequestCreateDto(6, "Void", null, reason.Data.Id, null))).Success.Should().BeFalse();

            _f.User.UserId = 2; _f.User.UserName = "admin@axislb.com"; _f.User.Roles = new[] { "admin" };
            var approved = await _f.Voids.ApproveAsync(r.Data.Id, new VoidDecisionDto("ok"));
            approved.Success.Should().BeTrue();
            approved.Data!.Status.Should().Be(VoidRequestStatuses.Approved);
            approved.Data.DecidedBy.Should().Be("admin@axislb.com");
            _f.Transactions.Verify(t => t.DeleteAsync(6, It.IsAny<CancellationToken>()), Times.Once);

            // Deciding twice is refused.
            (await _f.Voids.RejectAsync(r.Data.Id, new VoidDecisionDto(null))).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Refund_approval_pays_out_of_the_approvers_open_shift()
        {
            _f.AddPaidTransaction(7, 50m);
            var reason = await _f.Voids.CreateReasonAsync(new VoidReasonCreateDto("Refund", RequiresApproval: false));
            var r = await _f.Voids.CreateAsync(new VoidRequestCreateDto(7, "Refund", 12.5m, reason.Data!.Id, null));
            // Refunds are never auto-approved: money leaves a drawer.
            r.Data!.Status.Should().Be(VoidRequestStatuses.Pending);

            _f.User.UserId = 2; _f.User.UserName = "admin@axislb.com"; _f.User.Roles = new[] { "admin" };
            (await _f.Voids.ApproveAsync(r.Data.Id, new VoidDecisionDto(null))).Success.Should().BeFalse("no open shift to pay from");

            var shift = await _f.Shifts.OpenAsync(new OpenShiftDto("Admin desk", 100m, null));
            var ok = await _f.Voids.ApproveAsync(r.Data.Id, new VoidDecisionDto(null));
            ok.Success.Should().BeTrue();
            _f.Transactions.Verify(t => t.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

            var current = await _f.Shifts.GetCurrentAsync();
            current!.Movements.Should().ContainSingle(m => m.Type == CashMovementTypes.PayOut && m.Amount == 12.5m && m.Reason == $"Refund #{r.Data.Id}");
            (await _f.Shifts.XReportAsync()).Data!.ExpectedCash.Should().Be(87.5m);
            shift.Data!.Id.Should().Be(current.Id);
        }

        [Fact]
        public async Task Rejects_requests_for_unknown_transactions_or_inactive_reasons()
        {
            var reason = await _f.Voids.CreateReasonAsync(new VoidReasonCreateDto("Old", IsActive: false));
            _f.AddPaidTransaction(8, 5m);
            (await _f.Voids.CreateAsync(new VoidRequestCreateDto(999, "Void", null, reason.Data!.Id, null))).Success.Should().BeFalse();
            (await _f.Voids.CreateAsync(new VoidRequestCreateDto(8, "Void", null, reason.Data.Id, null))).Success.Should().BeFalse();
            (await _f.Voids.CreateAsync(new VoidRequestCreateDto(8, "Cancel", null, reason.Data.Id, null))).Success.Should().BeFalse();
        }
    }
}
