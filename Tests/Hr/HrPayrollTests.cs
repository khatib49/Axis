using Application.DTOs;
using Application.Mapping;
using Application.Services;
using Application.Services.AccountingPlus;
using Application.Services.Hr;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Hr
{
    public class HrPayrollTests : IDisposable
    {
        private const int Harry = 1, Sally = 2, Olly = 3, Nina = 4;
        private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

        private readonly ApplicationDbContext _db = HrTestDb.New();
        private readonly JournalService _journal;
        private readonly AccountingPeriodService _periods;
        private readonly FakeUser _user = new();

        public HrPayrollTests()
        {
            _db.AddRange(
                new AccountType { Id = 1, TypeName = "Asset", NormalBalance = "Debit" },
                new AccountType { Id = 2, TypeName = "Liability", NormalBalance = "Credit" },
                new AccountType { Id = 5, TypeName = "Expense", NormalBalance = "Debit" },
                new Account { Id = 1, AccountNumber = "1000", AccountName = "Cash on Hand", AccountTypeId = 1 },
                new Account { Id = 2, AccountNumber = "2300", AccountName = "Loans Payable", AccountTypeId = 2 });
            _db.AddRange(
                new Employee { Id = Harry, FullName = "Hourly Harry", Position = "Barista", HourlyRate = 10m, IsActive = true, HiredOn = new DateOnly(2025, 1, 1) },
                new Employee { Id = Sally, FullName = "Salaried Sally", Position = "Manager", HourlyRate = 20m, MonthlySalary = 3000m, IsActive = true, HiredOn = new DateOnly(2025, 1, 1) },
                new Employee { Id = Olly, FullName = "Overtime Olly", HourlyRate = 10m, IsActive = true, HiredOn = new DateOnly(2025, 1, 1) },
                new Employee { Id = Nina, FullName = "No Rate Nina", IsActive = true, HiredOn = new DateOnly(2025, 1, 1) },
                new Employee { Id = 5, FullName = "Gone Gary", MonthlySalary = 999m, IsActive = false, HiredOn = new DateOnly(2025, 1, 1) });
            AddHours(Harry, 100);           // 100 h in November
            AddHours(Olly, 220);            // 220 h → 12 h overtime
            AddHours(Sally, 50);            // hours only reported for salaried staff
            var oct = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
            _db.Add(new AttendanceRecord { EmployeeId = Harry, ClockIn = oct, ClockOut = oct.AddHours(8), WorkedMinutes = 480 }); // other month: ignored
            _db.Add(new AttendanceRecord { EmployeeId = Harry, ClockIn = Now, ClockOut = null, WorkedMinutes = null });         // still open: ignored
            _db.SaveChanges();

            _journal = new JournalService(
                new BaseRepository<JournalEntry>(_db), new BaseRepository<JournalEntryLine>(_db), new BaseRepository<Account>(_db),
                new BaseRepository<TransactionRecord>(_db), new BaseRepository<Expense>(_db), new BaseRepository<Category>(_db),
                new BaseRepository<EventRegistration>(_db), new BaseRepository<OnlineOrder>(_db), new UnitOfWork(_db),
                new AccountingMapper(), NullLogger<JournalService>.Instance);
            _periods = new AccountingPeriodService(_db, _user, NullLogger<AccountingPeriodService>.Instance);
        }

        private void AddHours(int employeeId, int hours)
        {
            // 10-hour closed shifts starting Nov 1st, one per day.
            var day = 0;
            for (var left = hours; left > 0; left -= 10, day++)
            {
                var h = Math.Min(10, left);
                var start = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
                _db.Add(new AttendanceRecord { EmployeeId = employeeId, ClockIn = start, ClockOut = start.AddHours(h), WorkedMinutes = h * 60, Source = AttendanceRecord.SourceManual });
            }
        }

        public void Dispose() => _db.Dispose();

        private PayrollService Svc() => new(_db, _journal, _periods, _user, NullLogger<PayrollService>.Instance, () => Now);

        /// <summary>Harry gets a 50 transport allowance; Sally opts out of NSSF.</summary>
        private async Task SeedOverridesAsync(PayrollService svc)
        {
            var types = await svc.ListComponentTypesAsync();
            var transport = types.Single(t => t.Name == "Transport");
            var nssf = types.Single(t => t.Name == "NSSF employee");
            (await svc.UpsertEmployeeComponentAsync(Harry, transport.Id, new HrEmployeePayComponentUpsertDto(Amount: 50m))).Success.Should().BeTrue();
            (await svc.UpsertEmployeeComponentAsync(Sally, nssf.Id, new HrEmployeePayComponentUpsertDto(IsActive: false))).Success.Should().BeTrue();
        }

        private static HrPayrollLineDto Line(HrPayrollRunDto run, int employeeId) => run.Lines!.Single(l => l.EmployeeId == employeeId);

        [Fact]
        public void Pure_calculations_hourly_salaried_and_overtime()
        {
            var hourly = HrPayrollCalculations.ComputeBase(null, 10m, 100 * 60);
            hourly.Should().Be(new HrPayrollCalculations.BasePay(1000m, 100m, 0m, 0m, false));

            var salaried = HrPayrollCalculations.ComputeBase(3000m, 20m, 50 * 60);
            salaried.Should().Be(new HrPayrollCalculations.BasePay(3000m, 50m, 0m, 0m, true));

            var ot = HrPayrollCalculations.ComputeBase(null, 10m, 220 * 60);
            ot.Base.Should().Be(2080m); ot.OvertimeHours.Should().Be(12m); ot.OvertimePay.Should().Be(180m);

            HrPayrollCalculations.ComputeBase(null, null, 600).Base.Should().Be(0m);
            HrPayrollCalculations.ComponentAmount("PercentOfGross", 3m, 1050m).Should().Be(31.50m);
            HrPayrollCalculations.ComponentAmount("Fixed", 50m, 1050m).Should().Be(50m);
            HrPayrollCalculations.ComponentAmount("PercentOfGross", 3m, 33.33m).Should().Be(1.00m); // rounded to cents
        }

        [Fact]
        public async Task Generate_uses_hourly_or_salaried_base_and_overtime_from_closed_attendance()
        {
            var svc = Svc();
            var r = await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11));
            r.Success.Should().BeTrue(r.Message);
            var run = r.Data!;
            run.Status.Should().Be("Draft");
            run.Lines!.Select(l => l.EmployeeId).Should().BeEquivalentTo(new[] { Harry, Sally, Olly, Nina }); // inactive Gary skipped

            var h = Line(run, Harry);
            h.HoursWorked.Should().Be(100m); h.BasePay.Should().Be(1000m); h.OvertimeHours.Should().Be(0m);

            var s = Line(run, Sally);
            s.BasePay.Should().Be(3000m); s.HoursWorked.Should().Be(50m); s.OvertimePay.Should().Be(0m); s.MonthlySalary.Should().Be(3000m);

            var o = Line(run, Olly);
            o.HoursWorked.Should().Be(220m); o.BasePay.Should().Be(2080m); o.OvertimeHours.Should().Be(12m); o.OvertimePay.Should().Be(180m);

            var n = Line(run, Nina);
            n.BasePay.Should().Be(0m); n.Net.Should().Be(0m); n.Notes.Should().Contain("No monthly salary or hourly rate");

            foreach (var l in run.Lines!) l.Net.Should().Be(l.Gross - l.Deductions);
            run.TotalNet.Should().Be(run.TotalGross - run.TotalDeductions);
        }

        [Fact]
        public async Task Components_defaults_apply_to_everyone_and_employee_overrides_win()
        {
            var svc = Svc();
            var types = await svc.ListComponentTypesAsync();
            types.Select(t => t.Name).Should().BeEquivalentTo(new[] { "NSSF employee", "Transport", "Income tax" });
            types.Single(t => t.Name == "NSSF employee").Should().Match<HrPayComponentTypeDto>(t => t.Kind == "Deduction" && t.Method == "PercentOfGross" && t.Rate == 3m && t.IsStatutory && t.DefaultForAll);
            types.Single(t => t.Name == "Transport").DefaultForAll.Should().BeFalse();
            (await svc.ListComponentTypesAsync()).Should().HaveCount(3, "seeding happens once");

            await SeedOverridesAsync(svc);
            var effective = await svc.ListEmployeeComponentsAsync(Harry);
            effective!.Single(c => c.Name == "Transport").Should().Match<HrEmployeePayComponentDto>(c => c.Applies && c.HasOverride && c.EffectiveRate == 50m);
            effective.Single(c => c.Name == "NSSF employee").Should().Match<HrEmployeePayComponentDto>(c => c.Applies && !c.HasOverride && c.EffectiveRate == 3m);

            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            var h = Line(run, Harry);
            h.Allowances.Should().Be(50m);                       // override on a default-off component
            h.Gross.Should().Be(1050m);
            h.Deductions.Should().Be(31.50m);                    // 3% of gross incl. allowance
            h.StatutoryDeductions.Should().Be(31.50m);
            h.Net.Should().Be(1018.50m);
            h.Components.Should().Contain(c => c.Name == "Transport" && c.Source == "Override" && c.Amount == 50m);
            h.Components.Should().Contain(c => c.Name == "NSSF employee" && c.Source == "Default" && c.Amount == 31.50m && c.IsStatutory);

            var s = Line(run, Sally);
            s.Deductions.Should().Be(0m);                        // opted out of the default NSSF
            s.Net.Should().Be(3000m);

            var o = Line(run, Olly);
            o.Gross.Should().Be(2260m); o.Deductions.Should().Be(67.80m); o.Net.Should().Be(2192.20m);

            run.TotalGross.Should().Be(6310m);
            run.TotalStatutory.Should().Be(99.30m);
            run.TotalDeductions.Should().Be(99.30m);
            run.TotalNet.Should().Be(6210.70m);
        }

        [Fact]
        public async Task Advance_in_the_month_is_deducted_and_recovered_exactly_once()
        {
            var svc = Svc();
            var adv = await svc.CreateAdvanceAsync(new HrPayrollAdvanceUpsertDto(Harry, 200m, new DateOnly(2026, 11, 5), "rent"));
            adv.Success.Should().BeTrue(adv.Message);
            var octAdv = await svc.CreateAdvanceAsync(new HrPayrollAdvanceUpsertDto(Harry, 70m, new DateOnly(2026, 10, 20)));
            octAdv.Success.Should().BeTrue();

            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            var h = Line(run, Harry);
            h.AdvancesRecovered.Should().Be(200m);
            h.Deductions.Should().Be(230m);                      // 30 NSSF + 200 advance
            h.Net.Should().Be(770m);
            h.Components.Should().ContainSingle(c => c.Source == "Advance" && c.Amount == 200m);
            (await svc.ListAdvancesAsync(2026, 11, null)).Single().RecoveredInRunId.Should().Be(run.Id);
            (await svc.ListAdvancesAsync(2026, 10, null)).Single().RecoveredInRunId.Should().BeNull("October advances belong to October's run");

            // Regenerating the same month recovers it once, not twice.
            var again = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            again.Id.Should().Be(run.Id);
            Line(again, Harry).AdvancesRecovered.Should().Be(200m);
            (await _db.Set<PayrollAdvance>().CountAsync(a => a.RecoveredInRunId == run.Id)).Should().Be(1);

            // A recovered advance can no longer be edited or deleted.
            (await svc.DeleteAdvanceAsync(adv.Data!.Id)).Success.Should().BeFalse();
            (await svc.UpdateAdvanceAsync(adv.Data.Id, new HrPayrollAdvanceUpsertDto(Harry, 1m, new DateOnly(2026, 11, 5)))).Success.Should().BeFalse();

            // December's run does not see November's advance.
            var dec = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 12))).Data!;
            Line(dec, Harry).AdvancesRecovered.Should().Be(0m);
        }

        [Fact]
        public async Task One_run_per_month_regenerate_keeps_exclusions_and_adjustments_and_is_refused_after_approval()
        {
            var svc = Svc();
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            var nina = Line(run, Nina);
            var harry = Line(run, Harry);
            (await svc.AdjustLineAsync(run.Id, nina.Id, new HrPayrollLineAdjustDto(IsExcluded: true, Notes: "unpaid intern"))).Success.Should().BeTrue();
            var adj = await svc.AdjustLineAsync(run.Id, harry.Id, new HrPayrollLineAdjustDto(ManualAllowance: 100m, ManualDeduction: 25m));
            adj.Success.Should().BeTrue(adj.Message);
            adj.Data!.Gross.Should().Be(1100m);
            adj.Data.Deductions.Should().Be(33m + 25m);           // NSSF 3% of 1100 + manual deduction
            adj.Data.Net.Should().Be(1042m);
            adj.Data.Components.Should().Contain(c => c.Source == "Manual" && c.Kind == "Allowance" && c.Amount == 100m);

            var regen = await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11));
            regen.Success.Should().BeTrue(regen.Message);
            regen.Data!.Id.Should().Be(run.Id);
            (await svc.ListRunsAsync(2026)).Should().ContainSingle();
            var n2 = Line(regen.Data, Nina);
            n2.IsExcluded.Should().BeTrue(); n2.Notes.Should().Be("unpaid intern");
            var h2 = Line(regen.Data, Harry);
            h2.ManualAllowance.Should().Be(100m); h2.ManualDeduction.Should().Be(25m); h2.Net.Should().Be(1042m);
            regen.Data.ExcludedCount.Should().Be(1);
            regen.Data.EmployeeCount.Should().Be(3);

            (await svc.ApproveAsync(run.Id)).Success.Should().BeTrue();
            var after = await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11));
            after.Success.Should().BeFalse();
            after.Message.Should().Contain("Approved");
            (await svc.AdjustLineAsync(run.Id, harry.Id, new HrPayrollLineAdjustDto(Notes: "x"))).Success.Should().BeFalse("approved runs are frozen");
            (await svc.DeleteRunAsync(run.Id)).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Approve_posts_one_balanced_entry_on_the_right_accounts_and_is_idempotent()
        {
            var svc = Svc();
            await SeedOverridesAsync(svc);
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;

            var r = await svc.ApproveAsync(run.Id);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.Status.Should().Be("Approved");
            r.Data.ApprovedBy.Should().Be(_user.UserName);
            r.Data.JournalEntryId.Should().NotBeNull();

            // Accounts were created because the test chart had none; 2300 (Loans Payable) was NOT reused.
            var accounts = await _db.Set<Account>().AsNoTracking().ToListAsync();
            var expense = accounts.Single(a => a.AccountName == PayrollService.ExpenseAccountName);
            var payable = accounts.Single(a => a.AccountName == PayrollService.PayableAccountName);
            var statutory = accounts.Single(a => a.AccountName == PayrollService.StatutoryAccountName);
            expense.AccountNumber.Should().Be("5400"); expense.AccountTypeId.Should().Be(5);
            payable.AccountNumber.Should().Be("2200"); payable.AccountTypeId.Should().Be(2);
            statutory.AccountNumber.Should().Be("2310"); statutory.AccountTypeId.Should().Be(2);

            var je = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Id == r.Data.JournalEntryId);
            je.ReferenceType.Should().Be("Payroll"); je.ReferenceId.Should().Be(run.Id);
            je.IsPosted.Should().BeTrue(); je.IsVoided.Should().BeFalse();
            je.EntryDate.Should().Be(Now, "the month is still running so the entry is dated today");
            je.Lines.Sum(l => l.DebitAmount).Should().Be(je.Lines.Sum(l => l.CreditAmount));
            je.Lines.Single(l => l.AccountId == expense.Id).DebitAmount.Should().Be(6310m);
            je.Lines.Single(l => l.AccountId == statutory.Id).CreditAmount.Should().Be(99.30m);
            je.Lines.Single(l => l.AccountId == payable.Id).CreditAmount.Should().Be(6210.70m);

            var byRef = await _journal.GetJournalEntriesByReferenceAsync("Payroll", run.Id);
            byRef.Data.Should().ContainSingle();

            var second = await svc.ApproveAsync(run.Id);
            second.Success.Should().BeTrue();
            second.Data!.JournalEntryId.Should().Be(r.Data.JournalEntryId);
            (await _db.Set<JournalEntry>().CountAsync(e => e.ReferenceType == "Payroll")).Should().Be(1);
        }

        [Fact]
        public async Task Approve_reuses_existing_salary_accounts_when_the_chart_has_them()
        {
            _db.AddRange(
                new Account { Id = 10, AccountNumber = "5400", AccountName = "Salaries & Wages Expense", AccountTypeId = 5 },
                new Account { Id = 11, AccountNumber = "2200", AccountName = "Salaries Payable", AccountTypeId = 2 });
            await _db.SaveChangesAsync();
            var svc = Svc();
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            (await svc.ApproveAsync(run.Id)).Success.Should().BeTrue();
            var je = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.ReferenceType == "Payroll");
            je.Lines.Should().Contain(l => l.AccountId == 10 && l.DebitAmount == run.TotalGross);
            je.Lines.Should().Contain(l => l.AccountId == 11 && l.CreditAmount == run.TotalGross - run.TotalStatutory);
            (await _db.Set<Account>().CountAsync(a => a.AccountName.Contains("Salaries"))).Should().Be(2, "nothing new was created for salaries");
        }

        [Fact]
        public async Task Mark_paid_posts_payable_against_cash_and_requires_approval()
        {
            var svc = Svc();
            await SeedOverridesAsync(svc);
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            (await svc.MarkPaidAsync(run.Id, new HrPayrollMarkPaidDto())).Success.Should().BeFalse("draft runs cannot be paid");
            (await svc.ApproveAsync(run.Id)).Success.Should().BeTrue();

            (await svc.MarkPaidAsync(run.Id, new HrPayrollMarkPaidDto("9999"))).Message.Should().Contain("9999");

            var r = await svc.MarkPaidAsync(run.Id, new HrPayrollMarkPaidDto());
            r.Success.Should().BeTrue(r.Message);
            r.Data!.Status.Should().Be("Paid");
            r.Data.PaidOn.Should().Be(Now);
            r.Data.PaymentJournalEntryId.Should().NotBeNull().And.NotBe(r.Data.JournalEntryId);

            var payable = await _db.Set<Account>().AsNoTracking().SingleAsync(a => a.AccountName == PayrollService.PayableAccountName);
            var je = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Id == r.Data.PaymentJournalEntryId);
            je.ReferenceType.Should().Be("PayrollPayment"); je.ReferenceId.Should().Be(run.Id); je.IsPosted.Should().BeTrue();
            je.Lines.Should().HaveCount(2);
            je.Lines.Single(l => l.AccountId == payable.Id).DebitAmount.Should().Be(6210.70m);
            je.Lines.Single(l => l.AccountId == 1).CreditAmount.Should().Be(6210.70m); // 1000 Cash on Hand

            var again = await svc.MarkPaidAsync(run.Id, new HrPayrollMarkPaidDto());
            again.Success.Should().BeTrue();
            again.Data!.PaymentJournalEntryId.Should().Be(r.Data.PaymentJournalEntryId);
            (await _db.Set<JournalEntry>().CountAsync(e => e.ReferenceType == "PayrollPayment")).Should().Be(1);
        }

        [Fact]
        public async Task Locked_period_refuses_approve_and_mark_paid()
        {
            var svc = Svc();
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            (await _periods.LockAsync(new PeriodLockRequestDto(2026, 11, "closed"))).Success.Should().BeTrue();

            var r = await svc.ApproveAsync(run.Id);
            r.Success.Should().BeFalse();
            r.Error.Should().Be("period_locked");
            r.Message.Should().Contain("2026-11");
            (await svc.GetRunAsync(run.Id))!.Status.Should().Be("Draft");
            (await _db.Set<JournalEntry>().CountAsync()).Should().Be(0);

            (await _periods.UnlockAsync(2026, 11)).Success.Should().BeTrue();
            (await svc.ApproveAsync(run.Id)).Success.Should().BeTrue();

            (await _periods.LockAsync(new PeriodLockRequestDto(2026, 11, "closed again"))).Success.Should().BeTrue();
            var paid = await svc.MarkPaidAsync(run.Id, new HrPayrollMarkPaidDto(PaidOn: new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)));
            paid.Success.Should().BeFalse();
            paid.Error.Should().Be("period_locked");
            // Paying in an open month (December) is fine.
            (await svc.MarkPaidAsync(run.Id, new HrPayrollMarkPaidDto(PaidOn: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)))).Success.Should().BeTrue();
        }

        [Fact]
        public async Task Csv_has_the_documented_columns_and_one_row_per_line()
        {
            var svc = Svc();
            await SeedOverridesAsync(svc);
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            (await svc.AdjustLineAsync(run.Id, Line(run, Nina).Id, new HrPayrollLineAdjustDto(IsExcluded: true))).Success.Should().BeTrue();

            var csv = await svc.ExportCsvAsync(run.Id);
            var lines = csv!.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            lines[0].Should().Be("employee,position,basePay,hoursWorked,overtimeHours,overtimePay,allowances,deductions,gross,net,excluded");
            lines.Should().HaveCount(5);
            lines.Should().Contain("Hourly Harry,Barista,1000.00,100.00,0.00,0.00,50.00,31.50,1050.00,1018.50,no");
            lines.Should().Contain("Overtime Olly,,2080.00,220.00,12.00,180.00,0.00,67.80,2260.00,2192.20,no");
            lines.Should().Contain("No Rate Nina,,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,yes");
            (await svc.ExportCsvAsync(999)).Should().BeNull();
        }

        [Fact]
        public async Task Payslips_split_allowances_from_deductions_and_render_html()
        {
            var svc = Svc();
            await SeedOverridesAsync(svc);
            var run = (await svc.GenerateAsync(new HrPayrollGenerateDto(2026, 11))).Data!;
            var slips = await svc.PayslipsAsync(run.Id);
            slips.Should().HaveCount(4);
            var h = slips!.Single(s => s.EmployeeId == Harry);
            h.Allowances.Should().ContainSingle(a => a.Name == "Transport" && a.Amount == 50m);
            h.Deductions.Should().ContainSingle(d => d.Name == "NSSF employee" && d.IsStatutory);
            h.Net.Should().Be(1018.50m);

            var html = svc.PayslipHtml((await svc.PayslipAsync(run.Id, Harry))!);
            html.Should().StartWith("<!doctype html>").And.Contain("Hourly Harry").And.Contain("November 2026").And.Contain("1,018.50").And.Contain("Transport");
            (await svc.PayslipAsync(run.Id, 999)).Should().BeNull();
        }
    }
}
