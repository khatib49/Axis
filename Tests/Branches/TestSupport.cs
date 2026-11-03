using Application.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.Branches
{
    internal sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId { get; set; }
        public string UserName { get; set; } = "tester@axislb.com";
        public IReadOnlyList<string> Roles { get; set; } = new List<string>();
        public int? BranchId { get; set; }
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    internal static class TestDb
    {
        public static ApplicationDbContext New() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
    }
}
