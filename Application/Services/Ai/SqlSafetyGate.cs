using System.Text.RegularExpressions;

namespace Application.Services.Ai
{
    /// <summary>
    /// Hard guard for run_select_query — we let Claude write arbitrary SQL,
    /// so this is the only thing between the AI and a destructive statement.
    /// Approach:
    ///   1. Strip comments (so DML hidden inside /* */ or -- can't sneak by).
    ///   2. Require the first non-whitespace keyword to be SELECT or WITH.
    ///   3. Reject any semicolons (no statement chaining).
    ///   4. Reject standalone DML/DDL keywords anywhere in the body.
    /// All checks are belt-and-suspenders — the production-grade move on top
    /// would be a Postgres role with only SELECT grants and a separate
    /// connection string, but this gate catches the obvious vectors today.
    /// </summary>
    public static class SqlSafetyGate
    {
        private static readonly string[] BannedTokens = new[]
        {
            @"\binsert\b", @"\bupdate\b", @"\bdelete\b", @"\bdrop\b", @"\btruncate\b",
            @"\balter\b",  @"\bcreate\b", @"\bgrant\b",  @"\brevoke\b", @"\bvacuum\b",
            @"\bcopy\b",   @"\breindex\b", @"\bdo\s+\$\$", @"\bcall\b", @"\bexecute\b"
        };

        /// <summary>
        /// Throws InvalidOperationException if the SQL is unsafe. Returns
        /// the cleaned-up SQL (comments stripped, trailing semicolon removed)
        /// if it passes. Caller should use the original SQL for execution —
        /// the return value is for diagnostic/test purposes.
        /// </summary>
        public static string EnsureSelectOnly(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException("Empty SQL.");

            var cleaned = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"--.*?$", " ", RegexOptions.Multiline);
            var trimmed = cleaned.Trim().TrimEnd(';').Trim();

            if (!Regex.IsMatch(trimmed, @"^\s*(select|with)\b", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Only SELECT statements are allowed.");

            if (trimmed.Contains(';'))
                throw new InvalidOperationException("Multiple statements are not allowed.");

            foreach (var pat in BannedTokens)
                if (Regex.IsMatch(trimmed, pat, RegexOptions.IgnoreCase))
                    throw new InvalidOperationException($"Disallowed token detected: {pat}");

            return trimmed;
        }

        public static bool IsSafe(string sql)
        {
            try { EnsureSelectOnly(sql); return true; }
            catch { return false; }
        }
    }
}
