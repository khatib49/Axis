using Application.DTOs;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Stock
{
    /// <summary>
    /// The Stock Movements / Waste Log search runs on the server over every
    /// movement in the filters, so the total and the paging reflect it.
    /// </summary>
    public class MovementSearchTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly IngredientService _svc;

        public MovementSearchTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            _db.AddRange(
                new Ingredient { Id = 1, Name = "Mozzarella", Unit = "kg" },
                new Ingredient { Id = 2, Name = "Penne", Unit = "kg" });

            var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            // 60 movements of penne consumption; a few distinctive ones spread through.
            for (var i = 1; i <= 60; i++)
            {
                _db.Add(new StockMovement
                {
                    Id = i,
                    IngredientId = i == 55 ? 1 : 2,
                    Quantity = -1,
                    Type = i == 40 ? "Waste" : "Consumption",
                    WasteReason = i == 40 ? "Expired" : null,
                    Notes = i == 33 ? "Monthly count" : null,
                    ReferenceType = i == 20 ? "Purchase" : "Transaction",
                    ReferenceId = 18000 + i,
                    BalanceAfter = 100 - i,
                    CreatedBy = "chef@axislb.com",
                    CreatedOn = day.AddHours(i),
                });
            }
            _db.SaveChanges();

            _svc = new IngredientService(
                new BaseRepository<Ingredient>(_db), new BaseRepository<StockMovement>(_db),
                new BaseRepository<RecipeLine>(_db), new BaseRepository<PurchaseLine>(_db),
                new UnitOfWork(_db), NullLogger<IngredientService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private Task<PaginatedResponse<StockMovementDto>> Search(string? q, string? type = null)
            => _svc.GetMovementsAsync(new StockMovementFilterDto(null, type, null, null, 1, 10, q));

        [Theory]
        [InlineData("MOZZA", 55)]     // ingredient, case-insensitive, beyond the first page
        [InlineData("expired", 40)]   // waste reason
        [InlineData("monthly", 33)]   // notes
        [InlineData("purchase", 20)]  // reference type
        [InlineData("18047", 47)]     // reference id
        public async Task Search_finds_matches_across_all_pages(string q, int expectedId)
        {
            var r = await Search(q);
            r.TotalCount.Should().Be(1);
            r.Data.Select(m => m.Id).Should().Equal(expectedId);
        }

        [Fact]
        public async Task Search_combines_with_the_type_filter()
        {
            (await Search("expired", "Waste")).Data.Select(m => m.Id).Should().Equal(40);
            (await Search("expired", "Consumption")).TotalCount.Should().Be(0);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        public async Task Blank_search_returns_everything(string? q)
        {
            var r = await Search(q);
            r.TotalCount.Should().Be(60);
            r.Data.Should().HaveCount(10);
        }
    }
}
