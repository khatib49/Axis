using System.Text.RegularExpressions;

namespace Application.Services.Marketing
{
    /// <summary>{{placeholder}} rendering; names are case-insensitive and may have inner spaces. Unknown placeholders render empty.</summary>
    public static class MarketingTemplate
    {
        private static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

        public static string Render(string? template, IReadOnlyDictionary<string, string?> values)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return Placeholder.Replace(template, m =>
                values.TryGetValue(m.Groups[1].Value, out var v) ? v ?? "" : "");
        }

        public static bool Has(string? template, string placeholder) =>
            !string.IsNullOrEmpty(template) &&
            Placeholder.Matches(template).Any(m => string.Equals(m.Groups[1].Value, placeholder, StringComparison.OrdinalIgnoreCase));

        public static Dictionary<string, string?> Values(string? name, string? firstName, DateTime? lastVisit, string? code, string? unsubscribeUrl) =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name ?? "",
                ["firstName"] = firstName ?? "",
                ["lastVisit"] = lastVisit?.ToString("yyyy-MM-dd") ?? "",
                ["code"] = code ?? "",
                ["unsubscribeUrl"] = unsubscribeUrl ?? "",
            };
    }
}
