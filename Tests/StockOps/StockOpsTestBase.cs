using Application.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.StockOps
{
    internal sealed class TestCurrentUser : ICurrentUserAccessor
    {
        public int? UserId => 1;
        public string UserName => "stock@axislb.com";
        public IReadOnlyList<string> Roles => new[] { "admin" };
        public int? BranchId => null;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    internal static class StockOpsTestDb
    {
        public static ApplicationDbContext Create()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
    }
}
