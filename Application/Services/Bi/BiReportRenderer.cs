using System.Net;
using System.Text;
using Application.DTOs;

namespace Application.Services.Bi
{
    /// <summary>Pure rendering of a ReportPayload to Html or Csv. Static so it is trivially testable.</summary>
    public static class BiReportRenderer
    {
        public static string Render(ReportPayload p, string? format) =>
            string.Equals(format, "Csv", StringComparison.OrdinalIgnoreCase) ? RenderCsv(p) : RenderHtml(p);

        /// <summary>RFC 4180 field: quoted when it holds a comma, quote, CR or LF; quotes doubled.</summary>
        public static string EscapeCsv(string? value)
        {
            if (value is null) return "";
            var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                              || value.StartsWith(' ') || value.EndsWith(' ');
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static string RenderCsv(ReportPayload p)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", p.Columns.Select(EscapeCsv))).Append("\r\n");
            foreach (var row in p.Rows)
                sb.Append(string.Join(",", row.Select(EscapeCsv))).Append("\r\n");
            if (p.Summary.Count > 0)
            {
                sb.Append("\r\n");
                foreach (var kv in p.Summary)
                    sb.Append(EscapeCsv(kv.Key)).Append(',').Append(EscapeCsv(kv.Value)).Append("\r\n");
            }
            return sb.ToString();
        }

        public static string RenderHtml(ReportPayload p)
        {
            static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2937;font-size:14px\">");
            sb.Append("<h2 style=\"margin:0 0 4px 0\">").Append(H(p.Title)).Append("</h2>");
            sb.Append("<div style=\"color:#6b7280;font-size:12px;margin-bottom:12px\">Generated ")
              .Append(p.GeneratedOn.ToString("yyyy-MM-dd HH:mm")).Append(" UTC</div>");
            if (p.Summary.Count > 0)
            {
                sb.Append("<table style=\"border-collapse:collapse;margin-bottom:14px\">");
                foreach (var kv in p.Summary)
                    sb.Append("<tr><td style=\"padding:3px 12px 3px 0;color:#6b7280\">").Append(H(kv.Key))
                      .Append("</td><td style=\"padding:3px 0;font-weight:bold\">").Append(H(kv.Value)).Append("</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("<table style=\"border-collapse:collapse;width:100%;max-width:800px\"><thead><tr>");
            foreach (var c in p.Columns)
                sb.Append("<th style=\"text-align:left;border-bottom:2px solid #d1d5db;padding:6px 8px;background:#f3f4f6\">").Append(H(c)).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            if (p.Rows.Count == 0)
                sb.Append("<tr><td colspan=\"").Append(Math.Max(1, p.Columns.Count)).Append("\" style=\"padding:10px 8px;color:#6b7280\">No data.</td></tr>");
            foreach (var row in p.Rows)
            {
                sb.Append("<tr>");
                foreach (var cell in row)
                    sb.Append("<td style=\"border-bottom:1px solid #e5e7eb;padding:6px 8px\">").Append(H(cell)).Append("</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table></div>");
            return sb.ToString();
        }
    }
}
