namespace Application.Services.Hr
{
    /// <summary>Pure payroll arithmetic (no I/O) so the rules can be unit tested directly. All money rounded to 2 dp.</summary>
    public static class HrPayrollCalculations
    {
        /// <summary>Hours per month above which hourly staff earn overtime.</summary>
        public const decimal MonthlyOvertimeThresholdHours = 208m;
        public const decimal OvertimeMultiplier = 1.5m;

        public static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        public record BasePay(decimal Base, decimal Hours, decimal OvertimeHours, decimal OvertimePay, bool IsSalaried);

        /// <summary>
        /// Salaried staff: base = monthly salary, no overtime (hours are reported only).
        /// Hourly staff: base = hours up to the threshold × rate; hours above it × rate × 1.5.
        /// No salary and no rate: everything is zero.
        /// </summary>
        public static BasePay ComputeBase(decimal? monthlySalary, decimal? hourlyRate, int workedMinutes)
        {
            var hours = Math.Round(Math.Max(0, workedMinutes) / 60m, 2, MidpointRounding.AwayFromZero);
            if (monthlySalary.HasValue && monthlySalary.Value > 0)
                return new BasePay(Money(monthlySalary.Value), hours, 0m, 0m, true);
            var rate = hourlyRate ?? 0m;
            if (rate <= 0) return new BasePay(0m, hours, 0m, 0m, false);
            var regular = Math.Min(hours, MonthlyOvertimeThresholdHours);
            var ot = Math.Max(0m, hours - MonthlyOvertimeThresholdHours);
            return new BasePay(Money(regular * rate), hours, ot, Money(ot * rate * OvertimeMultiplier), false);
        }

        /// <summary>Amount of a component: fixed amount, or percentage of the given basis.</summary>
        public static decimal ComponentAmount(string method, decimal rate, decimal basis)
            => method == Domain.Entities.PayComponentType.MethodPercentOfGross
                ? Money(basis * rate / 100m)
                : Money(rate);
    }
}
