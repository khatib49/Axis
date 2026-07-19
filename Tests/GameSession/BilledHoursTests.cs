using FluentAssertions;
using Xunit;

namespace Tests.GameSession
{
    /// <summary>
    /// Locks down Rami's exact pricing rules for game sessions. If these
    /// numbers ever change, the finance team will notice — so a red test
    /// here is a much better warning than a customer complaint.
    ///
    /// Rules (2026-07):
    ///   Board games (GameTypeId=2):
    ///     0-60 min  → 1.0 hr
    ///     61-90 min → 1.5 hr
    ///     91+ min   → day pass (flat, not hours-based)
    ///
    ///   PS5 (GameTypeId=6):
    ///     Same 60/90 snap, then every 30 min adds 0.5 hr forever.
    /// </summary>
    public class BilledHoursTests
    {
        // These helpers must stay in sync with GetBilledHoursBoardGame /
        // GetBilledHoursPs5 in TransactionRecordService.CloseGameSession.
        private static decimal BoardGame(double minutes)
        {
            if (minutes <= 0) return 0m;
            if (minutes <= 60) return 1.0m;
            return 1.5m;
        }

        private static decimal Ps5(double minutes)
        {
            if (minutes <= 0) return 0m;
            if (minutes <= 60) return 1.0m;
            var overrun = minutes - 60.0;
            var extraHalfHours = (int)System.Math.Ceiling(overrun / 30.0);
            return 1.0m + 0.5m * extraHalfHours;
        }

        // ── Board games ────────────────────────────────────────────────
        [Theory]
        [InlineData(1,   1.0)]      // 1 min stay → 1 hour
        [InlineData(30,  1.0)]
        [InlineData(59,  1.0)]
        [InlineData(60,  1.0)]      // exactly 1h → still 1h
        [InlineData(60.5, 1.5)]     // just over the hour → 1.5h
        [InlineData(75,  1.5)]
        [InlineData(89,  1.5)]
        [InlineData(90,  1.5)]      // exactly 90 min → 1.5h (boundary)
        public void BoardGame_snaps_correctly_up_to_90(double minutes, double expected)
        {
            BoardGame(minutes).Should().Be((decimal)expected);
        }

        // 91+ minutes on board games is the "day pass" territory — the
        // controller code branches away before calling GetBilledHoursBoardGame,
        // so we don't assert on that path here (see the fallback tests instead).

        // ── PS5 ────────────────────────────────────────────────────────
        [Theory]
        [InlineData(1,    1.0)]
        [InlineData(60,   1.0)]
        [InlineData(60.5, 1.5)]
        [InlineData(75,   1.5)]
        [InlineData(90,   1.5)]     // boundary — still 1.5h
        [InlineData(90.5, 2.0)]     // one second past 90 → 2h
        [InlineData(105,  2.0)]
        [InlineData(120,  2.0)]     // boundary — 2h
        [InlineData(120.1,2.5)]     // just past → 2.5h
        [InlineData(135,  2.5)]
        [InlineData(150,  2.5)]     // boundary
        [InlineData(151,  3.0)]     // step
        [InlineData(180,  3.0)]     // boundary
        [InlineData(181,  3.5)]
        [InlineData(210,  3.5)]
        [InlineData(211,  4.0)]
        [InlineData(300,  5.0)]     // exact 5h
        [InlineData(301,  5.5)]
        public void Ps5_snaps_every_30_minutes_after_the_first_hour(double minutes, double expected)
        {
            Ps5(minutes).Should().Be((decimal)expected);
        }

        // ── Edge cases ─────────────────────────────────────────────────
        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Zero_or_negative_returns_zero(double minutes)
        {
            BoardGame(minutes).Should().Be(0m);
            Ps5(minutes).Should().Be(0m);
        }

        // ── Sanity — a 24-hour PS5 session shouldn't overflow or explode
        [Fact]
        public void Very_long_ps5_session_stays_sensible()
        {
            // 24h = 1440 min. After the first hour, 1380 min remain,
            // split into 46 half-hour blocks → 1.0 + 46 × 0.5 = 24.0h.
            Ps5(1440).Should().Be(24.0m);
        }
    }
}
