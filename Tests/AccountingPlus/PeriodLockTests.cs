using Application.DTOs;
using FluentAssertions;
using Xunit;

namespace Tests.AccountingPlus
{
    public class PeriodLockTests : IDisposable
    {
        private readonly AccountingPlusTestHost _h = new();
        private static readonly DateTime InSept = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime InOct = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _h.Dispose();

        [Fact]
        public async Task Locked_month_blocks_bills_payments_and_depreciation_but_other_months_still_post()
        {
            var lockResult = await _h.Periods.LockAsync(new PeriodLockRequestDto(2026, 9, "closed"));
            lockResult.Success.Should().BeTrue();
            (await _h.Periods.EnsureOpenAsync(InSept)).Success.Should().BeFalse();
            (await _h.Periods.EnsureOpenAsync(new DateTime(2026, 9, 30, 23, 59, 0, DateTimeKind.Utc))).Success.Should().BeFalse();
            (await _h.Periods.EnsureOpenAsync(InOct)).Success.Should().BeTrue();

            var blocked = await _h.Bills.CreateAsync(new SupplierBillCreateDto(1, "S1", InSept, InOct, 10, 0, AccountingPlusTestHost.Expense, null, null, null, "Open"));
            blocked.Success.Should().BeFalse();
            blocked.Error.Should().Contain("2026-09");

            var draftOk = await _h.Bills.CreateAsync(new SupplierBillCreateDto(1, "S1", InSept, InOct, 10, 0, AccountingPlusTestHost.Expense, null, null, null, "Draft"));
            draftOk.Success.Should().BeTrue("drafts post nothing");
            (await _h.Bills.OpenAsync(draftOk.Data!.Id)).Success.Should().BeFalse();

            var octBill = await _h.Bills.CreateAsync(new SupplierBillCreateDto(1, "O1", InOct, InOct, 10, 0, AccountingPlusTestHost.Expense, null, null, null, "Open"));
            octBill.Success.Should().BeTrue(octBill.Error);
            (await _h.Bills.AddPaymentAsync(octBill.Data!.Id, new SupplierBillPaymentCreateDto(InSept, 5, "Cash", null, AccountingPlusTestHost.Cash))).Success.Should().BeFalse("payment dated in locked month");
            (await _h.Bills.AddPaymentAsync(octBill.Data!.Id, new SupplierBillPaymentCreateDto(InOct, 5, "Cash", null, AccountingPlusTestHost.Cash))).Success.Should().BeTrue();

            var asset = (await _h.Assets.CreateAsync(new FixedAssetUpsertDto("Desk", null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 120, 0, 12, "StraightLine",
                AccountingPlusTestHost.AssetGl, AccountingPlusTestHost.AccumDep, AccountingPlusTestHost.DepExpense, null))).Data!;
            (await _h.Assets.RunDepreciationAsync(2026, 9, asset.Id)).Success.Should().BeFalse();
            (await _h.Assets.RunDepreciationAsync(2026, 10, asset.Id)).Success.Should().BeTrue();

            (await _h.Periods.UnlockAsync(2026, 9)).Success.Should().BeTrue();
            (await _h.Assets.RunDepreciationAsync(2026, 9, asset.Id)).Success.Should().BeTrue();

            var list = await _h.Periods.ListAsync(2026);
            list.Should().HaveCount(12);
            list.Should().OnlyContain(p => !p.IsLocked);
        }
    }
}
