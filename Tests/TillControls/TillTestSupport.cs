using Application.Common;
using Application.IServices;
using Application.Services.TillControls;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests.TillControls
{
    /// <summary>Stand-in for the JWT-backed accessor: tests pick who is calling.</summary>
    public sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId { get; set; } = 1;
        public string UserName { get; set; } = "cashier@axislb.com";
        public IReadOnlyList<string> Roles { get; set; } = new[] { "cashier" };
        public int? BranchId { get; set; }
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public sealed class TillFixture : IDisposable
    {
        public ApplicationDbContext Db { get; }
        public FakeUser User { get; } = new();
        public Mock<ITransactionRecordService> Transactions { get; } = new();
        public CurrencyService Currency { get; }
        public ShiftReportService Reports { get; }
        public ShiftService Shifts { get; }
        public VoidRequestService Voids { get; }
        public TenderService Tenders { get; }

        public TillFixture()
        {
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            Db.Add(new Status { Id = 6, Name = "Paid" });
            Db.Add(new AppUser { Id = 1, UserName = "cashier@axislb.com", StatusId = 6 });
            Db.Add(new AppUser { Id = 2, UserName = "admin@axislb.com", StatusId = 6 });
            Db.SaveChanges();

            Currency = new CurrencyService(Db, User, NullLogger<CurrencyService>.Instance);
            Reports = new ShiftReportService(Db);
            Shifts = new ShiftService(Db, User, Reports, NullLogger<ShiftService>.Instance);
            Voids = new VoidRequestService(Db, User, Transactions.Object, NullLogger<VoidRequestService>.Instance);
            Tenders = new TenderService(Db, User, Currency, NullLogger<TenderService>.Instance);
        }

        public TransactionRecord AddPaidTransaction(int id, decimal total, string createdBy = "cashier@axislb.com", DateTime? paidOn = null)
        {
            var t = new TransactionRecord
            {
                Id = id, StatusId = 6, TotalPrice = total, CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow, PaidOn = paidOn ?? DateTime.UtcNow,
            };
            Db.Add(t);
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return t;
        }

        public void Dispose() => Db.Dispose();
    }
}
