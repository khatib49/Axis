using System.Security.Claims;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Tests.Branches
{
    /// <summary>BranchStampInterceptor: header > caller's default UserBranch > system default; never throws.</summary>
    public class BranchStampInterceptorTests
    {
        private sealed class FakeHttp : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; }
        }

        private static FakeHttp Http(int? userId = null, string? header = null)
        {
            var ctx = new DefaultHttpContext();
            if (userId.HasValue)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }, "test"));
            if (header is not null) ctx.Request.Headers["X-Branch-Id"] = header;
            return new FakeHttp { HttpContext = ctx };
        }

        private static ApplicationDbContext Db(IHttpContextAccessor http, string? name = null) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new BranchStampInterceptor(http))
            .Options);

        private static void SeedBranches(ApplicationDbContext db)
        {
            db.AddRange(
                new Branch { Id = 1, Code = "HQ", Name = "Hamra", IsDefault = true },
                new Branch { Id = 2, Code = "JB", Name = "Jbeil" },
                new Branch { Id = 3, Code = "TR", Name = "Tripoli" });
            db.Add(new UserBranch { Id = 1, UserId = 42, BranchId = 2, IsDefault = true });
            db.Add(new UserBranch { Id = 2, UserId = 42, BranchId = 3, IsDefault = false });
            db.Add(new UserBranch { Id = 3, UserId = 43, BranchId = 3, IsDefault = false });
            db.SaveChanges();
        }

        private static TransactionRecord Tx(int id, int? branchId = null) =>
            new() { Id = id, StatusId = 7, TotalPrice = 10, CreatedBy = "t", BranchId = branchId };

        [Fact]
        public async Task Header_wins_over_user_default_and_system_default()
        {
            using var db = Db(Http(userId: 42, header: "3"));
            SeedBranches(db);
            db.Add(Tx(1));
            await db.SaveChangesAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(3);
        }

        [Fact]
        public async Task Users_default_branch_is_used_when_no_header()
        {
            using var db = Db(Http(userId: 42));
            SeedBranches(db);
            db.Add(Tx(1));
            await db.SaveChangesAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(2);
        }

        [Fact]
        public async Task Users_first_assignment_is_used_when_none_is_flagged_default()
        {
            using var db = Db(Http(userId: 43));
            SeedBranches(db);
            db.Add(Tx(1));
            await db.SaveChangesAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(3);
        }

        [Fact]
        public async Task System_default_branch_is_used_for_anonymous_or_unassigned_callers()
        {
            using var db = Db(Http(userId: 99));
            SeedBranches(db);
            db.Add(Tx(1));
            await db.SaveChangesAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(1);

            using var db2 = Db(new FakeHttp { HttpContext = null });
            SeedBranches(db2);
            db2.Add(Tx(1));
            db2.SaveChanges();
            db2.Set<TransactionRecord>().AsNoTracking().Single(t => t.Id == 1).BranchId.Should().Be(1);
        }

        [Fact]
        public async Task Unknown_header_branch_falls_through_to_the_next_rule()
        {
            using var db = Db(Http(userId: 42, header: "999"));
            SeedBranches(db);
            db.Add(Tx(1));
            await db.SaveChangesAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(2);
        }

        [Fact]
        public async Task Explicit_branch_is_never_overwritten_and_modified_rows_are_ignored()
        {
            using var db = Db(Http(userId: 42, header: "3"));
            SeedBranches(db);
            db.Add(Tx(1, branchId: 1));
            await db.SaveChangesAsync();
            var row = await db.Set<TransactionRecord>().SingleAsync(t => t.Id == 1);
            row.BranchId.Should().Be(1);

            row.TotalPrice = 20;
            await db.SaveChangesAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(1);
        }

        [Fact]
        public async Task Never_throws_when_no_branch_exists_and_leaves_null()
        {
            using var db = Db(Http(userId: 42, header: "1"));
            db.Add(Tx(1));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().NotThrowAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().BeNull();
        }

        [Fact]
        public async Task Never_throws_when_the_http_accessor_is_broken()
        {
            var broken = new Moq.Mock<IHttpContextAccessor>();
            broken.SetupGet(h => h.HttpContext).Throws(new InvalidOperationException("boom"));
            using var db = Db(broken.Object);
            SeedBranches(db);
            db.Add(Tx(1));
            var act = async () => await db.SaveChangesAsync();
            await act.Should().NotThrowAsync();
            (await db.Set<TransactionRecord>().AsNoTracking().SingleAsync(t => t.Id == 1)).BranchId.Should().BeNull();
        }
    }
}
