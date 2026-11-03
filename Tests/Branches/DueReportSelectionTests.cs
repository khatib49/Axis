using Application.Services.Bi;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Branches
{
    /// <summary>The hourly job picks reports whose RunAtHourUtc (+ DayOfWeek / day 1) matches.</summary>
    public class DueReportSelectionTests
    {
        // Monday 2026-10-05 06:15 UTC
        private static readonly DateTime MondayAt6 = new(2026, 10, 5, 6, 15, 0, DateTimeKind.Utc);
        // Thursday 2026-10-01 06:15 UTC (day 1 of the month)
        private static readonly DateTime FirstOfMonthAt6 = new(2026, 10, 1, 6, 15, 0, DateTimeKind.Utc);

        private static ScheduledReport Report(string freq, int hour, int? dow = null, bool active = true, DateTime? lastRun = null) =>
            new() { Name = "r", ReportType = "DailySales", Frequency = freq, RunAtHourUtc = hour, DayOfWeek = dow, IsActive = active, LastRunOn = lastRun, Recipients = "x@y.z" };

        [Theory]
        [InlineData(6, true)]
        [InlineData(7, false)]
        [InlineData(5, false)]
        public void Daily_is_due_only_in_its_hour(int hour, bool expected) =>
            ScheduledReportService.IsDue(Report("Daily", hour), MondayAt6).Should().Be(expected);

        [Fact]
        public void Weekly_needs_hour_and_day_of_week()
        {
            ScheduledReportService.IsDue(Report("Weekly", 6, (int)DayOfWeek.Monday), MondayAt6).Should().BeTrue();
            ScheduledReportService.IsDue(Report("Weekly", 6, (int)DayOfWeek.Tuesday), MondayAt6).Should().BeFalse();
            ScheduledReportService.IsDue(Report("Weekly", 9, (int)DayOfWeek.Monday), MondayAt6).Should().BeFalse();
            ScheduledReportService.IsDue(Report("Weekly", 6, null), MondayAt6).Should().BeFalse();
        }

        [Fact]
        public void Monthly_runs_on_the_first_only()
        {
            ScheduledReportService.IsDue(Report("Monthly", 6), FirstOfMonthAt6).Should().BeTrue();
            ScheduledReportService.IsDue(Report("Monthly", 6), MondayAt6).Should().BeFalse();
            ScheduledReportService.IsDue(Report("Monthly", 7), FirstOfMonthAt6).Should().BeFalse();
        }

        [Fact]
        public void Inactive_or_already_run_this_hour_is_not_due()
        {
            ScheduledReportService.IsDue(Report("Daily", 6, active: false), MondayAt6).Should().BeFalse();
            ScheduledReportService.IsDue(Report("Daily", 6, lastRun: MondayAt6.AddMinutes(-10)), MondayAt6).Should().BeFalse();
            ScheduledReportService.IsDue(Report("Daily", 6, lastRun: MondayAt6.AddDays(-1)), MondayAt6).Should().BeTrue();
        }
    }
}
