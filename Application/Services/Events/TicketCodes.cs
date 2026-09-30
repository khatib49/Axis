using System.Security.Cryptography;

namespace Application.Services.Events
{
    /// <summary>
    /// Event ticket codes: "TK-" + 10 chars from an alphabet without 0/O/1/I
    /// so they can be read aloud at the door. ~49 bits of entropy — the code
    /// is the only key to the public ticket page, so it must not be guessable.
    /// </summary>
    public static class TicketCodes
    {
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const string Prefix = "TK-";
        public const int BodyLength = 10;

        public static string NewRandom()
        {
            var bytes = RandomNumberGenerator.GetBytes(BodyLength);
            return Prefix + new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
        }

        /// <summary>
        /// Accepts what a scanner or a human types: the bare code, lowercase,
        /// a full ticket URL, or the 10-char body without the TK- prefix.
        /// Returns "" when nothing usable is left.
        /// </summary>
        public static string Normalise(string? raw)
        {
            var s = (raw ?? "").Trim().TrimEnd('/');
            if (s.Length == 0) return "";
            var slash = s.LastIndexOf('/');
            if (slash >= 0) s = s[(slash + 1)..];
            var q = s.IndexOf('?');
            if (q >= 0) s = s[..q];
            var hash = s.IndexOf('#');
            if (hash >= 0) s = s[..hash];
            s = s.Trim().ToUpperInvariant().Replace(" ", "");
            if (s.Length == BodyLength && !s.StartsWith(Prefix, StringComparison.Ordinal)) s = Prefix + s;
            return s;
        }

        public static bool LooksValid(string? code)
        {
            var s = code ?? "";
            return s.Length == Prefix.Length + BodyLength
                && s.StartsWith(Prefix, StringComparison.Ordinal)
                && s[Prefix.Length..].All(c => Alphabet.Contains(c) || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'));   // hex allowed: SQL backfill codes
        }
    }
}
