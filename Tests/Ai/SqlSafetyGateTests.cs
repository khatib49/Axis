using Application.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Tests.Ai
{
    /// <summary>
    /// Tests for the only thing between Claude and a destructive SQL
    /// statement. Each test is one attack vector or one valid pattern.
    /// </summary>
    public class SqlSafetyGateTests
    {
        // ── Valid statements ───────────────────────────────────────────
        [Theory]
        [InlineData("SELECT 1")]
        [InlineData("select * from \"Items\"")]
        [InlineData("SELECT name FROM \"Items\" WHERE \"Price\" > 0")]
        [InlineData("WITH t AS (SELECT 1 AS x) SELECT * FROM t")]
        [InlineData("SELECT name FROM \"Items\" ORDER BY name LIMIT 10")]
        // Trailing semicolon allowed (we strip exactly one).
        [InlineData("SELECT 1;")]
        // Newlines + indentation
        [InlineData("SELECT\n  id, name\nFROM \"Items\"\nLIMIT 5")]
        public void Allows_clean_selects(string sql)
        {
            SqlSafetyGate.IsSafe(sql).Should().BeTrue();
            var act = () => SqlSafetyGate.EnsureSelectOnly(sql);
            act.Should().NotThrow();
        }

        // ── Direct DML/DDL — should always be blocked ──────────────────
        [Theory]
        [InlineData("DELETE FROM \"Items\"")]
        [InlineData("delete from items where id = 1")]
        [InlineData("UPDATE \"Items\" SET \"Price\" = 0")]
        [InlineData("INSERT INTO \"Items\" (\"Name\") VALUES ('x')")]
        [InlineData("DROP TABLE \"Items\"")]
        [InlineData("TRUNCATE \"TransactionRecords\"")]
        [InlineData("ALTER TABLE \"Items\" DROP COLUMN \"Price\"")]
        [InlineData("CREATE TABLE foo (id int)")]
        [InlineData("GRANT ALL ON \"Items\" TO public")]
        [InlineData("REVOKE ALL ON \"Items\" FROM public")]
        [InlineData("VACUUM FULL")]
        public void Rejects_dml_and_ddl(string sql)
        {
            SqlSafetyGate.IsSafe(sql).Should().BeFalse();
            var act = () => SqlSafetyGate.EnsureSelectOnly(sql);
            act.Should().Throw<InvalidOperationException>();
        }

        // ── Sneaky comment-based evasion ───────────────────────────────
        [Fact]
        public void Rejects_dml_hidden_inside_block_comment()
        {
            // The /* */ is removed during cleanup, so the DELETE becomes visible.
            var sql = "SELECT 1 /* harmless */; DELETE FROM \"Items\"";
            SqlSafetyGate.IsSafe(sql).Should().BeFalse();
        }

        [Fact]
        public void Rejects_dml_after_line_comment()
        {
            var sql = "SELECT 1 -- pretend everything is fine\n; DELETE FROM \"Items\"";
            SqlSafetyGate.IsSafe(sql).Should().BeFalse();
        }

        // ── Statement chaining ─────────────────────────────────────────
        [Fact]
        public void Rejects_chained_statements_with_semicolon()
        {
            var sql = "SELECT 1; SELECT 2";
            SqlSafetyGate.IsSafe(sql).Should().BeFalse();
        }

        // ── Function calls that imply writes ───────────────────────────
        [Theory]
        [InlineData("CALL refresh_views()")]
        [InlineData("DO $$ BEGIN PERFORM 1; END $$")]
        public void Rejects_call_and_do_blocks(string sql)
        {
            SqlSafetyGate.IsSafe(sql).Should().BeFalse();
        }

        // ── Garbage input ──────────────────────────────────────────────
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Rejects_empty(string sql)
        {
            var act = () => SqlSafetyGate.EnsureSelectOnly(sql);
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void Rejects_starts_with_garbage()
        {
            SqlSafetyGate.IsSafe("yolo SELECT 1").Should().BeFalse();
        }
    }
}
