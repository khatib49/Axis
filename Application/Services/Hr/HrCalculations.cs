namespace Application.Services.Hr
{
    /// <summary>Pure HR calculations (no I/O) so they can be unit tested directly.</summary>
    public static class HrCalculations
    {
        /// <summary>Whole minutes the clock-in is after the scheduled start; never negative.</summary>
        public static int LateMinutes(DateTime scheduledStart, DateTime clockIn)
        {
            var diff = clockIn - scheduledStart;
            return diff <= TimeSpan.Zero ? 0 : (int)Math.Floor(diff.TotalMinutes);
        }

        public static int WorkedMinutes(DateTime clockIn, DateTime clockOut)
        {
            var diff = clockOut - clockIn;
            return diff <= TimeSpan.Zero ? 0 : (int)Math.Round(diff.TotalMinutes, MidpointRounding.AwayFromZero);
        }

        /// <summary>Inclusive calendar days between two dates.</summary>
        public static int LeaveDays(DateOnly from, DateOnly to)
            => to < from ? 0 : to.DayNumber - from.DayNumber + 1;

        /// <summary>
        /// Splits <paramref name="total"/> across the given weights, rounding each share to
        /// 2 decimals; any rounding remainder (positive or negative) goes to the largest share
        /// (first one on ties) so the allocations always sum exactly to the total.
        /// When all weights are zero the split is equal.
        /// </summary>
        public static IReadOnlyList<(int EmployeeId, decimal Amount)> Distribute(
            decimal total, IReadOnlyList<(int EmployeeId, decimal Weight)> weights)
        {
            if (weights.Count == 0) return Array.Empty<(int, decimal)>();
            var sumW = weights.Sum(w => w.Weight);
            var effective = sumW <= 0
                ? weights.Select(w => (w.EmployeeId, Weight: 1m)).ToList()
                : weights.Select(w => (w.EmployeeId, Weight: Math.Max(0m, w.Weight))).ToList();
            sumW = effective.Sum(w => w.Weight);

            var result = effective
                .Select(w => (w.EmployeeId, Amount: Math.Round(total * w.Weight / sumW, 2, MidpointRounding.AwayFromZero)))
                .ToList();

            var remainder = total - result.Sum(r => r.Amount);
            if (remainder != 0)
            {
                var idx = 0;
                for (var i = 1; i < result.Count; i++)
                    if (result[i].Amount > result[idx].Amount) idx = i;
                result[idx] = (result[idx].EmployeeId, result[idx].Amount + remainder);
            }
            return result;
        }
    }
}
