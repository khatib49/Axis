using Application.Services.Branches;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Branches
{
    /// <summary>GET api/branches/me: admin → all, assigned → assigned, nothing → the default branch.</summary>
    public class BranchMeTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();

        public BranchMeTests()
        {
            _db.AddRange(
                new Branch { Id = 1, Code = "HQ", Name = "Hamra", IsDefault = true },
                new Branch { Id = 2, Code = "JB", Name = "Jbeil" },
                new Branch { Id = 3, Code = "OLD", Name = "Closed", IsActive = false });
            _db.Add(new UserBranch { Id = 1, UserId = 42, BranchId = 2, IsDefault = true });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private BranchService Svc(FakeUser u) => new(_db, u, NullLogger<BranchService>.Instance);

        [Fact]
        public async Task Admin_sees_every_active_branch()
        {
            var me = await Svc(new FakeUser { UserId = 1, Roles = new[] { "admin" } }).GetMineAsync();
            me.Source.Should().Be("admin");
            me.Branches.Select(b => b.Code).Should().BeEquivalentTo(new[] { "HQ", "JB" });
            me.DefaultBranchId.Should().Be(1);
        }

        [Fact]
        public async Task Assigned_user_sees_only_assigned_branches_with_their_own_default()
        {
            var me = await Svc(new FakeUser { UserId = 42, Roles = new[] { "cashier" } }).GetMineAsync();
            me.Source.Should().Be("assigned");
            me.Branches.Should().ContainSingle(b => b.Code == "JB");
            me.DefaultBranchId.Should().Be(2);
        }

        [Fact]
        public async Task Unassigned_user_falls_back_to_the_default_branch()
        {
            var me = await Svc(new FakeUser { UserId = 7, Roles = new[] { "cashier" } }).GetMineAsync();
            me.Source.Should().Be("default-fallback");
            me.Branches.Should().ContainSingle(b => b.Code == "HQ" && b.IsDefault);
            me.DefaultBranchId.Should().Be(1);
        }

        [Fact]
        public async Task Fallback_uses_first_active_branch_when_none_is_flagged_default()
        {
            var hq = _db.Set<Branch>().Single(b => b.Id == 1);
            hq.IsDefault = false;
            _db.SaveChanges();

            var me = await Svc(new FakeUser { UserId = 7, Roles = new[] { "cashier" } }).GetMineAsync();
            me.Source.Should().Be("default-fallback");
            me.Branches.Should().HaveCount(1);
            me.Branches[0].IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task Consolidated_attributes_legacy_transactions_to_the_default_branch()
        {
            var day = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
            _db.AddRange(
                new TransactionRecord { Id = 1, StatusId = 6, TotalPrice = 30, CreatedOn = day.AddHours(10), PaidOn = day.AddHours(10), CreatedBy = "a" },
                new TransactionRecord { Id = 2, StatusId = 6, TotalPrice = 10, CreatedOn = day.AddHours(12), PaidOn = day.AddHours(12), CreatedBy = "a" },
                new TransactionRecord { Id = 3, StatusId = 7, TotalPrice = 25, CreatedOn = day.AddHours(13), CreatedBy = "b" },
                new TransactionRecord { Id = 4, StatusId = 6, TotalPrice = 99, CreatedOn = day.AddDays(-3), PaidOn = day.AddDays(-3), CreatedBy = "a" });
            _db.SaveChanges();

            var dash = await Svc(new FakeUser { Roles = new[] { "admin" } }).GetConsolidatedAsync(day, day);

            dash.Attribution.Should().Be("default-branch");
            var hq = dash.Branches.Single(b => b.Code == "HQ");
            hq.Sales.Should().Be(40);
            hq.Orders.Should().Be(2);
            hq.AverageTicket.Should().Be(20);
            hq.OpenInvoices.Should().Be(1);
            hq.OpenInvoicesValue.Should().Be(25);
            dash.Branches.Single(b => b.Code == "JB").Sales.Should().Be(0);
            dash.Total.Sales.Should().Be(40);
        }
    }
}
