using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs;

namespace Application.Services.Marketing
{
    /// <summary>One evaluated audience member (client user + sales + loyalty facts).</summary>
    public record AudienceFacts(int UserId, string Name, string FirstName, string? Email, string? Phone,
        DateTime? LastVisit, decimal SpendAll, int VisitsAll, decimal SpendInPeriod, int VisitsInPeriod, int LoyaltyTickets);

    /// <summary>Pure rule evaluation (unit-tested); the data loading lives in MarketingSegmentService.</summary>
    public static class SegmentRules
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNameCaseInsensitive = true,
        };

        public static SegmentRulesDto Parse(string? rulesJson)
        {
            if (string.IsNullOrWhiteSpace(rulesJson)) return new SegmentRulesDto();
            try { return JsonSerializer.Deserialize<SegmentRulesDto>(rulesJson, Json) ?? new SegmentRulesDto(); }
            catch (JsonException) { return new SegmentRulesDto(); }
        }

        public static string Serialize(SegmentRulesDto? rules) => JsonSerializer.Serialize(rules ?? new SegmentRulesDto(), Json);

        /// <summary>Normalises nonsense values (negatives → null) so the engine never has to guard.</summary>
        public static SegmentRulesDto Clean(SegmentRulesDto? r)
        {
            r ??= new SegmentRulesDto();
            static int? Pos(int? v) => v is > 0 ? v : null;
            return new SegmentRulesDto(
                LastVisitWithinDays: Pos(r.LastVisitWithinDays),
                LastVisitOlderThanDays: Pos(r.LastVisitOlderThanDays),
                MinSpend: r.MinSpend is > 0 ? r.MinSpend : null,
                SpendPeriodDays: Pos(r.SpendPeriodDays),
                MinVisits: Pos(r.MinVisits),
                LoyaltyTicketsMin: Pos(r.LoyaltyTicketsMin),
                HasEmail: r.HasEmail,
                HasPhone: r.HasPhone,
                BranchId: Pos(r.BranchId));
        }

        /// <summary>
        /// All present rules must hold (AND). Visit rules use the last paid transaction;
        /// minSpend / minVisits use the spendPeriodDays window when set, else all time.
        /// lastVisitOlderThanDays also matches customers who never visited (inactive).
        /// </summary>
        public static bool Matches(SegmentRulesDto rules, AudienceFacts f, DateTime nowUtc)
        {
            var r = Clean(rules);
            if (r.LastVisitWithinDays is int within)
            {
                if (f.LastVisit is null || f.LastVisit.Value < nowUtc.AddDays(-within)) return false;
            }
            if (r.LastVisitOlderThanDays is int older)
            {
                if (f.LastVisit is not null && f.LastVisit.Value >= nowUtc.AddDays(-older)) return false;
            }
            var spend = r.SpendPeriodDays is null ? f.SpendAll : f.SpendInPeriod;
            var visits = r.SpendPeriodDays is null ? f.VisitsAll : f.VisitsInPeriod;
            if (r.MinSpend is decimal minSpend && spend < minSpend) return false;
            if (r.MinVisits is int minVisits && visits < minVisits) return false;
            if (r.LoyaltyTicketsMin is int minTickets && f.LoyaltyTickets < minTickets) return false;
            if (r.HasEmail == true && string.IsNullOrWhiteSpace(f.Email)) return false;
            if (r.HasEmail == false && !string.IsNullOrWhiteSpace(f.Email)) return false;
            if (r.HasPhone == true && string.IsNullOrWhiteSpace(f.Phone)) return false;
            if (r.HasPhone == false && !string.IsNullOrWhiteSpace(f.Phone)) return false;
            // BranchId is applied while loading transactions (only that branch's sales count).
            return true;
        }
    }
}
