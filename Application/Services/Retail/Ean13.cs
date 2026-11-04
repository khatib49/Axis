namespace Application.Services.Retail
{
    /// <summary>
    /// EAN-13 helpers. In-house codes use the GS1 "restricted circulation"
    /// prefix 200 (never assigned to a manufacturer), then the item id on
    /// 6 digits and the variant id on 3 (000 = plain item), then the
    /// standard modulo-10 check digit.
    /// </summary>
    public static class Ean13
    {
        public const string VenuePrefix = "200";

        /// <summary>Check digit for a 12-digit payload (odd positions ×1, even ×3, from the left).</summary>
        public static int CheckDigit(string payload12)
        {
            if (payload12 == null || payload12.Length != 12 || !payload12.All(char.IsDigit))
                throw new ArgumentException("EAN-13 payload must be exactly 12 digits.", nameof(payload12));
            var sum = 0;
            for (var i = 0; i < 12; i++)
            {
                var d = payload12[i] - '0';
                sum += (i % 2 == 0) ? d : d * 3;
            }
            return (10 - sum % 10) % 10;
        }

        public static bool IsValid(string code)
            => code != null && code.Length == 13 && code.All(char.IsDigit)
               && CheckDigit(code[..12]) == code[12] - '0';

        /// <summary>Builds the 13-digit in-house code, or null when the ids don't fit.</summary>
        public static string? Build(int itemId, int? variantId)
        {
            if (itemId <= 0 || itemId > 999_999) return null;
            var v = variantId ?? 0;
            if (v < 0 || v > 999) return null;
            var payload = $"{VenuePrefix}{itemId:D6}{v:D3}";
            return payload + CheckDigit(payload);
        }

        /// <summary>Trims and removes every whitespace character (scanners sometimes add CR/LF or spaces).</summary>
        public static string Normalise(string? raw)
            => raw == null ? string.Empty : new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}
