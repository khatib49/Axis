using Domain.Entities;

namespace Application.Services.Promotions
{
    /// <summary>CSV helpers shared by the promotion rules ("1,2,3" → ints).</summary>
    public static class PromoCsv
    {
        public static HashSet<int> ParseInts(string? csv)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var n)) set.Add(n);
            return set;
        }

        /// <summary>Normalises "3, 1,1,x" to "1,3" (or null when nothing parses).</summary>
        public static string? Normalise(string? csv)
        {
            var ints = ParseInts(csv);
            return ints.Count == 0 ? null : string.Join(",", ints.OrderBy(i => i));
        }
    }

    /// <summary>One line of an order as the coupon / happy-hour engines see it.</summary>
    public record PromoLine(int ItemId, int CategoryId, decimal UnitPrice, int Quantity)
    {
        public decimal Amount => UnitPrice * Quantity;
    }

    public record CouponEvaluation(bool Eligible, string? Reason, decimal OrderAmount, decimal EligibleAmount, decimal DiscountAmount);

    /// <summary>
    /// Pure coupon rules. Everything the DB must answer (how many times this
    /// customer already used the code) is passed in, so the rules are unit
    /// testable without a context.
    /// </summary>
    public static class CouponEngine
    {
        /// <param name="orderAmount">The order total the coupon is validated against.</param>
        /// <param name="categoryAmounts">Amount per category id in the order; may be null when unknown (whole order then counts).</param>
        /// <param name="customerUses">How many times the given customer already redeemed this coupon (0 when unknown).</param>
        public static CouponEvaluation Evaluate(
            Coupon c, decimal orderAmount, IReadOnlyDictionary<int, decimal>? categoryAmounts,
            int customerUses, DateTime nowUtc)
        {
            if (!c.IsActive)
                return Fail("Coupon is not active.", orderAmount);
            if (c.ValidFrom.HasValue && nowUtc < c.ValidFrom.Value)
                return Fail($"Coupon is not valid before {c.ValidFrom.Value:yyyy-MM-dd HH:mm} UTC.", orderAmount);
            if (c.ValidTo.HasValue && nowUtc > c.ValidTo.Value)
                return Fail("Coupon has expired.", orderAmount);
            if (c.MaxUses.HasValue && c.UsedCount >= c.MaxUses.Value)
                return Fail("Coupon has reached its maximum number of uses.", orderAmount);
            if (c.MaxUsesPerCustomer.HasValue && customerUses >= c.MaxUsesPerCustomer.Value)
                return Fail("This customer has already used the coupon the maximum number of times.", orderAmount);
            if (orderAmount <= 0)
                return Fail("Order amount must be greater than zero.", orderAmount);
            if (c.MinOrderAmount.HasValue && orderAmount < c.MinOrderAmount.Value)
                return Fail($"Order must be at least {c.MinOrderAmount.Value:F2} to use this coupon.", orderAmount);

            var categories = PromoCsv.ParseInts(c.CategoryIdsCsv);
            decimal eligible;
            if (categories.Count == 0 || categoryAmounts is null)
            {
                eligible = orderAmount;
            }
            else
            {
                eligible = categoryAmounts.Where(kv => categories.Contains(kv.Key)).Sum(kv => kv.Value);
                if (eligible <= 0)
                    return Fail("None of the items on the order belong to the categories this coupon covers.", orderAmount);
                // Other promotions may already have lowered the order below the
                // raw line total; never discount more than what is left.
                eligible = Math.Min(eligible, orderAmount);
            }

            decimal discount;
            if (string.Equals(c.Type, CouponType.Fixed, StringComparison.OrdinalIgnoreCase))
                discount = Math.Min(c.Value, eligible);
            else
                discount = eligible * Math.Clamp(c.Value, 0m, 100m) / 100m;

            if (c.MaxDiscountAmount.HasValue && discount > c.MaxDiscountAmount.Value)
                discount = c.MaxDiscountAmount.Value;

            discount = Math.Round(Math.Max(0m, Math.Min(discount, orderAmount)), 2);
            if (discount <= 0)
                return Fail("The coupon yields no discount on this order.", orderAmount);

            return new CouponEvaluation(true, null, orderAmount, eligible, discount);
        }

        private static CouponEvaluation Fail(string reason, decimal orderAmount)
            => new(false, reason, orderAmount, 0m, 0m);

        public static IReadOnlyDictionary<int, decimal> CategoryAmounts(IEnumerable<PromoLine> lines)
            => lines.GroupBy(l => l.CategoryId).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
    }

    /// <summary>Pure happy-hour window / matching logic, all in venue local time.</summary>
    public static class HappyHourEngine
    {
        public const string VenueTimeZoneId = "Asia/Beirut";

        public static TimeZoneInfo VenueTimeZone()
        {
            foreach (var id in new[] { VenueTimeZoneId, "Middle East Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }

        public static DateTime ToLocal(DateTime utc, TimeZoneInfo? tz = null)
            => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz ?? VenueTimeZone());

        /// <summary>ISO weekday: Monday = 1 … Sunday = 7.</summary>
        public static int IsoDay(DayOfWeek d) => d == DayOfWeek.Sunday ? 7 : (int)d;

        /// <summary>
        /// Is the rule in effect at <paramref name="local"/>? A window whose
        /// StartTime is later than its EndTime crosses midnight; its weekday
        /// is the day it STARTED, so "Fri 22:00–02:00" is active Sat 01:00.
        /// </summary>
        public static bool IsActiveAt(HappyHourRule rule, DateTime local)
        {
            if (!rule.IsActive) return false;
            if (rule.DiscountPercent <= 0) return false;

            var days = PromoCsv.ParseInts(rule.DaysOfWeekCsv);
            var t = TimeOnly.FromDateTime(local);
            var today = IsoDay(local.DayOfWeek);

            if (rule.StartTime == rule.EndTime)
                return days.Count == 0 || days.Contains(today); // all-day rule

            if (rule.StartTime < rule.EndTime)
            {
                var inWindow = t >= rule.StartTime && t < rule.EndTime;
                return inWindow && (days.Count == 0 || days.Contains(today));
            }

            // Overnight window.
            if (t >= rule.StartTime)
                return days.Count == 0 || days.Contains(today);
            if (t < rule.EndTime)
            {
                var startedOn = IsoDay(local.AddDays(-1).DayOfWeek);
                return days.Count == 0 || days.Contains(startedOn);
            }
            return false;
        }

        /// <summary>Does the rule cover this item? No category/item filter = everything.</summary>
        public static bool Matches(HappyHourRule rule, int itemId, int categoryId)
        {
            var items = PromoCsv.ParseInts(rule.ItemIdsCsv);
            var cats = PromoCsv.ParseInts(rule.CategoryIdsCsv);
            if (items.Count == 0 && cats.Count == 0) return true;
            return items.Contains(itemId) || cats.Contains(categoryId);
        }

        /// <summary>The rule that wins for an item: highest Priority, then biggest discount.</summary>
        public static HappyHourRule? BestFor(IEnumerable<HappyHourRule> activeRules, int itemId, int categoryId)
            => activeRules.Where(r => Matches(r, itemId, categoryId))
                          .OrderByDescending(r => r.Priority)
                          .ThenByDescending(r => r.DiscountPercent)
                          .FirstOrDefault();

        public static decimal DiscountedPrice(decimal price, decimal percent)
            => Math.Round(price * (1m - Math.Clamp(percent, 0m, 100m) / 100m), 2);

        /// <summary>Total happy-hour discount over a set of lines (each line uses its own best rule).</summary>
        public static (decimal Amount, int MatchedLines, HappyHourRule? TopRule) DiscountFor(
            IEnumerable<HappyHourRule> activeRules, IEnumerable<PromoLine> lines)
        {
            decimal amount = 0m; var matched = 0; HappyHourRule? top = null;
            foreach (var l in lines)
            {
                var rule = BestFor(activeRules, l.ItemId, l.CategoryId);
                if (rule is null) continue;
                matched++;
                amount += l.Amount * Math.Clamp(rule.DiscountPercent, 0m, 100m) / 100m;
                if (top is null || rule.Priority > top.Priority) top = rule;
            }
            return (Math.Round(amount, 2), matched, top);
        }

        public static bool TryParseTime(string? s, out TimeOnly t)
        {
            t = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return TimeOnly.TryParseExact(s.Trim(), new[] { "HH:mm", "H:mm", "HH:mm:ss" }, null,
                System.Globalization.DateTimeStyles.None, out t);
        }
    }

    /// <summary>Pure combo pricing.</summary>
    public static class ComboEngine
    {
        public static decimal ItemsTotal(IEnumerable<(decimal UnitPrice, int Quantity)> lines)
            => Math.Round(lines.Sum(l => l.UnitPrice * l.Quantity), 2);

        /// <summary>
        /// Signed delta to book on the invoice: negative when the combo is cheaper
        /// than its parts, 0 when it is not (a combo never makes the bill higher
        /// than the parts — the lines were already charged at full price).
        /// </summary>
        public static decimal PriceDelta(decimal comboPrice, decimal itemsTotal)
        {
            var delta = Math.Round(comboPrice - itemsTotal, 2);
            return delta < 0 ? delta : 0m;
        }
    }
}
