using Application.DTOs;

namespace Application.Services.AccountingPlus
{
    /// <summary>Date normalisation so Npgsql timestamptz columns always receive UTC.</summary>
    public static class AcctDates
    {
        public static DateTime Utc(DateTime d) => d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };

        public static DateTime? Utc(DateTime? d) => d.HasValue ? Utc(d.Value) : null;

        public static DateTime StartOfDay(DateTime d) => Utc(d).Date;
        public static DateTime EndOfDay(DateTime d) => Utc(d).Date.AddDays(1).AddTicks(-1);

        public static DateTime MonthStart(int year, int month) => new(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        public static DateTime MonthEnd(int year, int month) => MonthStart(year, month).AddMonths(1).AddTicks(-1);
    }

    /// <summary>Pure aging-bucket arithmetic shared by AP and AR aging.</summary>
    public static class AgingBuckets
    {
        public const string Current = "Current";
        public const string Days1To30 = "1-30";
        public const string Days31To60 = "31-60";
        public const string Days61To90 = "61-90";
        public const string Over90 = "90+";

        /// <summary>Bucket for an item that becomes "overdue" after <paramref name="referenceDate"/>.</summary>
        public static string Bucket(DateTime referenceDate, DateTime asOf)
        {
            var days = (asOf.Date - referenceDate.Date).Days;
            if (days <= 0) return Current;
            if (days <= 30) return Days1To30;
            if (days <= 60) return Days31To60;
            if (days <= 90) return Days61To90;
            return Over90;
        }

        public sealed class Accumulator
        {
            public decimal Current, D1To30, D31To60, D61To90, Over90;
            public int Count;

            public void Add(DateTime referenceDate, DateTime asOf, decimal amount)
            {
                Count++;
                switch (Bucket(referenceDate, asOf))
                {
                    case AgingBuckets.Current: Current += amount; break;
                    case Days1To30: D1To30 += amount; break;
                    case Days31To60: D31To60 += amount; break;
                    case Days61To90: D61To90 += amount; break;
                    default: Over90 += amount; break;
                }
            }

            public void Add(Accumulator other)
            {
                Count += other.Count;
                Current += other.Current; D1To30 += other.D1To30; D31To60 += other.D31To60; D61To90 += other.D61To90; Over90 += other.Over90;
            }

            public AgingBucketsDto ToDto() => new(
                Math.Round(Current, 2), Math.Round(D1To30, 2), Math.Round(D31To60, 2), Math.Round(D61To90, 2), Math.Round(Over90, 2),
                Math.Round(Current + D1To30 + D31To60 + D61To90 + Over90, 2));
        }
    }

    /// <summary>Pure VAT arithmetic.</summary>
    public static class VatMath
    {
        /// <summary>
        /// VAT contained in (inclusive) or added on top of (exclusive) <paramref name="amount"/>.
        /// Inclusive: amount × rate / (100 + rate). Exclusive: amount × rate / 100.
        /// </summary>
        public static decimal Vat(decimal amount, decimal ratePercent, bool inclusive)
        {
            if (ratePercent <= 0 || amount == 0) return 0m;
            var vat = inclusive
                ? amount * ratePercent / (100m + ratePercent)
                : amount * ratePercent / 100m;
            return Math.Round(vat, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>Net (ex-VAT) base of an amount.</summary>
        public static decimal Net(decimal amount, decimal ratePercent, bool inclusive)
            => inclusive ? amount - Vat(amount, ratePercent, true) : amount;
    }

    /// <summary>Pure depreciation schedules; the first period is the acquisition month.</summary>
    public static class DepreciationSchedule
    {
        public sealed record Row(int Year, int Month, decimal Amount, decimal AccumulatedAfter, decimal NetBookValueAfter);

        public static List<Row> Build(DateTime acquiredOn, decimal cost, decimal salvage, int usefulLifeMonths, string method)
        {
            var rows = new List<Row>();
            if (usefulLifeMonths <= 0 || cost <= salvage) return rows;

            var depreciable = cost - salvage;
            var year = acquiredOn.Year;
            var month = acquiredOn.Month;
            decimal accumulated = 0m;
            var declining = string.Equals(method, "DecliningBalance", StringComparison.OrdinalIgnoreCase);
            var monthlyRate = declining ? 2m / usefulLifeMonths : 0m; // double-declining, monthly
            var straight = Math.Round(depreciable / usefulLifeMonths, 2, MidpointRounding.AwayFromZero);

            for (var i = 1; i <= usefulLifeMonths; i++)
            {
                var remaining = depreciable - accumulated;
                if (remaining <= 0) break;

                decimal amount;
                if (i == usefulLifeMonths)
                    amount = remaining; // last month absorbs rounding / tail
                else if (declining)
                    amount = Math.Min(Math.Round((cost - accumulated) * monthlyRate, 2, MidpointRounding.AwayFromZero), remaining);
                else
                    amount = Math.Min(straight, remaining);

                if (amount <= 0 && !declining) break;
                accumulated += amount;
                rows.Add(new Row(year, month, amount, accumulated, cost - accumulated));

                month++;
                if (month > 12) { month = 1; year++; }
            }

            return rows;
        }
    }
}
