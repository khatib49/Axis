using Application.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.Hr
{
    internal sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId { get; set; } = 1;
        public string UserName { get; set; } = "admin@axislb.com";
        public IReadOnlyList<string> Roles { get; set; } = new[] { "admin" };
        public int? BranchId { get; set; }
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    internal static class HrTestDb
    {
        public static ApplicationDbContext New() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
    }
}
