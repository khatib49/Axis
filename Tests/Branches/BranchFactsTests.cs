using Application.Services.Branches;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Branches
{
    /// <summary>Consolidated dashboard grouping by TransactionRecord.BranchId and the legacy backfill.</summary>
    public class BranchFactsTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();
        private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        public BranchFactsTests()
        {
            _db.AddRange(
                new Branch { Id = 1, Code = "HQ", Name = "Hamra", IsDefault = true },
                new Branch { Id = 2, Code = "JB", Name = "Jbeil" });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private BranchService Svc() => new(_db, new FakeUser { Roles = new[] { "admin" } }, NullLogger<BranchService>.Instance);

        private static TransactionRecord Paid(int id, decimal total, int? branchId, DateTime? on = null) => new()
        {
            Id = id, StatusId = 6, TotalPrice = total, BranchId = branchId, CreatedBy = "a",
            CreatedOn = on ?? Day.AddHours(10), PaidOn = on ?? Day.AddHours(10),
        };

        private static TransactionRecord Open(int id, decimal total, int? branchId) => new()
        {
            Id = id, StatusId = 7, TotalPrice = total, BranchId = branchId, CreatedBy = "a", CreatedOn = Day.AddHours(12),
        };

        [Fact]
        public async Task Mixed_rows_group_by_branch_and_legacy_rows_land_on_the_default_branch()
        {
            _db.AddRange(
                Paid(1, 30, null), Paid(2, 10, 1), Paid(3, 50, 2), Paid(4, 70, 2),
                Paid(5, 99, 2, Day.AddDays(-3)),                 // outside the range
                Open(6, 25, null), Open(7, 5, 2));
            _db.SaveChanges();

            var dash = await Svc().GetConsolidatedAsync(Day, Day);

            dash.Attribution.Should().Be("transaction");
            dash.AttributionNote.Should().Contain("legacy");
            var hq = dash.Branches.Single(b => b.Code == "HQ");
            hq.Sales.Should().Be(40);
            hq.Orders.Should().Be(2);
            hq.AverageTicket.Should().Be(20);
            hq.OpenInvoices.Should().Be(1);
            hq.OpenInvoicesValue.Should().Be(25);
            var jb = dash.Branches.Single(b => b.Code == "JB");
            jb.Sales.Should().Be(120);
            jb.Orders.Should().Be(2);
            jb.AverageTicket.Should().Be(60);
            jb.OpenInvoices.Should().Be(1);
            jb.OpenInvoicesValue.Should().Be(5);
            dash.Total.Sales.Should().Be(160);
            dash.Total.Orders.Should().Be(4);
            dash.Total.OpenInvoices.Should().Be(2);
        }

        [Fact]
        public async Task Attribution_stays_default_branch_while_nothing_is_stamped()
        {
            _db.AddRange(Paid(1, 30, null), Open(2, 5, null));
            _db.SaveChanges();
            var dash = await Svc().GetConsolidatedAsync(Day, Day);
            dash.Attribution.Should().Be("default-branch");
            dash.Branches.Single(b => b.Code == "HQ").Sales.Should().Be(30);
        }

        [Fact]
        public async Task Attribution_is_transaction_without_a_legacy_note_when_every_row_is_stamped()
        {
            _db.AddRange(Paid(1, 30, 1), Paid(2, 20, 2));
            _db.SaveChanges();
            var dash = await Svc().GetConsolidatedAsync(Day, Day);
            dash.Attribution.Should().Be("transaction");
            dash.AttributionNote.Should().NotContain("legacy");
        }

        [Fact]
        public async Task Backfill_stamps_only_null_rows_with_the_default_branch_and_returns_the_count()
        {
            _db.AddRange(Paid(1, 30, null), Paid(2, 10, 2), Open(3, 5, null), Paid(4, 1, null, Day.AddDays(-30)));
            _db.SaveChanges();

            var r = await Svc().BackfillTransactionsAsync(null);

            r.Success.Should().BeTrue();
            r.Data.Should().Be(3);
            _db.Set<TransactionRecord>().Where(t => t.BranchId == 1).Select(t => t.Id).Should().BeEquivalentTo(new[] { 1, 3, 4 });
            _db.Set<TransactionRecord>().Single(t => t.Id == 2).BranchId.Should().Be(2);
            (await Svc().BackfillTransactionsAsync(null)).Data.Should().Be(0);
        }

        [Fact]
        public async Task Backfill_honours_an_explicit_branch_and_rejects_unknown_ones()
        {
            _db.AddRange(Paid(1, 30, null), Paid(2, 10, null));
            _db.SaveChanges();

            (await Svc().BackfillTransactionsAsync(999)).Success.Should().BeFalse();
            var r = await Svc().BackfillTransactionsAsync(2);
            r.Data.Should().Be(2);
            _db.Set<TransactionRecord>().All(t => t.BranchId == 2).Should().BeTrue();
        }
    }
}
