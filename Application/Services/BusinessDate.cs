namespace Application.Services
{
    /// <summary>
    /// A date the user picks (an entry's From/To, a drawing date) is a
    /// calendar day in Beirut, not an instant. Browsers used to send local
    /// midnight as UTC — 8 Aug 00:00 in Beirut arrives as
    /// "2026-08-07T21:00:00Z" — and taking .Date of that stored 7 Aug.
    ///
    /// ToDay() turns whatever arrives into the Beirut calendar day:
    ///   • "2026-08-08"                 (no zone)  → 8 Aug
    ///   • "2026-08-07T21:00:00Z"       (UTC)      → 8 Aug (Beirut 00:00)
    ///   • "2026-08-08T00:00:00Z"       (UTC)      → 8 Aug (Beirut 03:00)
    /// and returns it as midnight with Kind=Utc so it can be written to a
    /// timestamptz column unchanged.
    /// </summary>
    public static class BusinessDate
    {
        public static readonly TimeZoneInfo Zone = ResolveZone();

        private static TimeZoneInfo ResolveZone()
        {
            // IANA id on Linux / ICU-enabled Windows, Windows id otherwise.
            foreach (var id in new[] { "Asia/Beirut", "Middle East Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            // Fixed UTC+3 still maps Beirut midnight (UTC+2 or +3) and UTC
            // midnight to the right day, which is all ToDay() needs.
            return TimeZoneInfo.CreateCustomTimeZone("Beirut (fixed +03:00)", TimeSpan.FromHours(3), "Beirut", "Beirut");
        }

        public static DateTime ToDay(DateTime value)
        {
            var day = value.Kind switch
            {
                DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(value, Zone).Date,
                DateTimeKind.Local => TimeZoneInfo.ConvertTime(value, Zone).Date,
                _ => value.Date,
            };
            return DateTime.SpecifyKind(day, DateTimeKind.Utc);
        }
    }
}
