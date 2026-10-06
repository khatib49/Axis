using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Accounting
{
    /// <summary>
    /// Picking 8 Aug in Beirut must store 8 Aug. The old picker sent local
    /// midnight as UTC ("2026-08-07T21:00:00Z") and the API kept 7 Aug.
    /// </summary>
    public class BusinessDateTests
    {
        private static DateTime Parse(string json) => JsonSerializer.Deserialize<DateTime>($"\"{json}\"");

        [Theory]
        [InlineData("2026-08-08", "2026-08-08")]                 // new picker: plain calendar day
        [InlineData("2026-08-07T21:00:00.000Z", "2026-08-08")]   // old picker, summer (UTC+3)
        [InlineData("2026-01-07T22:00:00.000Z", "2026-01-08")]   // old picker, winter (UTC+2)
        [InlineData("2026-08-08T00:00:00Z", "2026-08-08")]       // stored value sent back on edit
        [InlineData("2026-08-08T00:00:00+03:00", "2026-08-08")]  // explicit offset
        [InlineData("2026-08-01T00:00:00", "2026-08-01")]        // no zone, with time
        public void Picked_date_is_the_Beirut_calendar_day(string sent, string expected)
        {
            var day = BusinessDate.ToDay(Parse(sent));

            day.Should().Be(DateTime.SpecifyKind(DateTime.Parse(expected), DateTimeKind.Utc));
            day.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Fact]
        public async Task Entry_saved_from_the_old_picker_keeps_the_day_that_was_picked()
        {
            var dbName = Guid.NewGuid().ToString();
            ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            using (var seed = NewDb())
            {
                seed.Set<ExpenseCategory>().Add(new ExpenseCategory { Id = 1, Name = "Rent" });
                seed.SaveChanges();
            }

            var journal = new Mock<IJournalService>();
            journal.Setup(j => j.CreateJournalEntryFromExpenseAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BaseResponse<JournalEntryOneDto>(true, null, null, null));
            // One context per call, like one DI scope per HTTP request.
            ExpenseService Svc(ApplicationDbContext db) => new(new BaseRepository<Expense>(db), new BaseRepository<ExpenseCategory>(db),
                new UnitOfWork(db), journal.Object, NullLogger<ExpenseService>.Instance);
            using var db1 = NewDb();
            using var db2 = NewDb();

            // 1–31 Aug picked in Beirut, sent the old way.
            var created = await Svc(db1).CreateAsync(new ExpenseCreateDto(1, 900m, "Cash", null,
                Parse("2026-07-31T21:00:00.000Z"), Parse("2026-08-30T21:00:00.000Z")), 1, default);

            created.FromDate.Date.Should().Be(new DateTime(2026, 8, 1));
            created.ToDate.Date.Should().Be(new DateTime(2026, 8, 31));

            // Editing without touching the dates sends the stored value back unchanged.
            var updated = await Svc(db2).UpdateAsync(created.Id, new ExpenseUpdateDto(950m, "Cash", null,
                Parse("2026-08-01T00:00:00Z"), Parse("2026-08-31T00:00:00Z"), 1), default);

            updated.FromDate.Date.Should().Be(new DateTime(2026, 8, 1));
            updated.ToDate.Date.Should().Be(new DateTime(2026, 8, 31));
        }
    }
}
