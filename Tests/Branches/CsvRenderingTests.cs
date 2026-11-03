using Application.DTOs;
using Application.Services.Bi;
using FluentAssertions;
using Xunit;

namespace Tests.Branches
{
    public class CsvRenderingTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("a,b", "\"a,b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("line1\nline2", "\"line1\nline2\"")]
        public void Fields_are_quoted_and_escaped_per_rfc4180(string? input, string expected) =>
            BiReportRenderer.EscapeCsv(input).Should().Be(expected);

        [Fact]
        public void Csv_has_header_rows_and_summary_block()
        {
            var p = new ReportPayload("Daily sales", "DailySales", DateTime.UtcNow,
                new[] { new KeyValuePair<string, string>("Total sales", "1,250.00") },
                new[] { "Category", "Quantity", "Amount" },
                new List<IReadOnlyList<string>>
                {
                    new[] { "Coffee, hot", "3", "12.00" },
                    new[] { "Board \"Games\"", "1", "5.00" },
                });

            var csv = BiReportRenderer.RenderCsv(p);
            var lines = csv.Split("\r\n");

            lines[0].Should().Be("Category,Quantity,Amount");
            lines[1].Should().Be("\"Coffee, hot\",3,12.00");
            lines[2].Should().Be("\"Board \"\"Games\"\"\",1,5.00");
            lines[3].Should().BeEmpty();
            lines[4].Should().Be("Total sales,\"1,250.00\"");
        }

        [Fact]
        public void Html_encodes_cell_content()
        {
            var p = new ReportPayload("T <b>", "LowStock", DateTime.UtcNow,
                Array.Empty<KeyValuePair<string, string>>(),
                new[] { "Name" },
                new List<IReadOnlyList<string>> { new[] { "<script>x</script>" } });
            var html = BiReportRenderer.RenderHtml(p);
            html.Should().Contain("&lt;script&gt;x&lt;/script&gt;").And.NotContain("<script>");
            html.Should().Contain("T &lt;b&gt;");
        }

        [Fact]
        public void Render_picks_format_case_insensitively()
        {
            var p = new ReportPayload("T", "LowStock", DateTime.UtcNow, Array.Empty<KeyValuePair<string, string>>(),
                new[] { "A" }, new List<IReadOnlyList<string>>());
            BiReportRenderer.Render(p, "csv").Should().StartWith("A\r\n");
            BiReportRenderer.Render(p, "HTML").Should().StartWith("<div");
        }
    }
}
