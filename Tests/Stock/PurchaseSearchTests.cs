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
    /// The Purchases page search runs on the server, over every purchase that
    /// matches the other filters — not only the page already loaded — so the
    /// total and the paging reflect the search.
    /// </summary>
    public class PurchaseSearchTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly PurchaseService _svc;

        public PurchaseSearchTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var farms = new Supplier { Id = 1, Name = "Fresh Farms" };
            var dairy = new Supplier { Id = 2, Name = "Beirut Dairy" };
            var mozz = new Ingredient { Id = 1, Name = "Mozzarella", Unit = "kg" };
            var penne = new Ingredient { Id = 2, Name = "Penne", Unit = "kg" };
            _db.AddRange(farms, dairy, mozz, penne);

            var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            // 30 purchases: only #7 and #25 are from the dairy; only #25 has mozzarella.
            for (var i = 1; i <= 30; i++)
            {
                var fromDairy = i == 7 || i == 25;
                _db.Add(new Purchase
                {
                    Id = i,
                    SupplierId = fromDairy ? 2 : 1,
                    PurchaseDate = day.AddDays(i % 20),
                    InvoiceNumber = i == 12 ? "INV-XYZ-12" : null,
                    Notes = i == 18 ? "Weekly restock" : null,
                    CreatedBy = "chef@axislb.com",
                    TotalCost = 10,
                    Lines = new List<PurchaseLine>
                    {
                        new() { Id = i, IngredientId = i == 25 ? 1 : 2, Quantity = 1, UnitCost = 10, LineTotal = 10 },
                    },
                });
            }
            _db.SaveChanges();

            _svc = new PurchaseService(
                new BaseRepository<Purchase>(_db), new BaseRepository<PurchaseLine>(_db),
                new BaseRepository<Supplier>(_db), new BaseRepository<Ingredient>(_db),
                new BaseRepository<StockMovement>(_db), new UnitOfWork(_db),
                NullLogger<PurchaseService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private Task<PaginatedResponse<PurchaseDto>> Search(string? q, int page = 1, int pageSize = 5)
            => _svc.ListAsync(new PurchaseFilterDto(null, null, null, null, page, pageSize, q));

        [Fact]
        public async Task Supplier_search_finds_matches_beyond_the_first_page_case_insensitively()
        {
            var r = await Search("DAIRY");
            r.TotalCount.Should().Be(2);
            r.Data.Select(p => p.Id).Should().BeEquivalentTo(new[] { 7, 25 });
        }

        [Theory]
        [InlineData("mozza", 25)]       // a line's ingredient
        [InlineData("xyz-12", 12)]      // invoice number
        [InlineData("weekly", 18)]      // notes
        public async Task Search_matches_ingredient_invoice_and_notes(string q, int expectedId)
        {
            var r = await Search(q);
            r.Data.Select(p => p.Id).Should().Equal(expectedId);
        }

        [Fact]
        public async Task Search_combines_with_the_other_filters()
        {
            var r = await _svc.ListAsync(new PurchaseFilterDto(2, 1, null, null, 1, 25, "dairy"));
            r.Data.Select(p => p.Id).Should().Equal(25);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task Blank_search_returns_everything(string? q)
        {
            var r = await Search(q);
            r.TotalCount.Should().Be(30);
            r.Data.Should().HaveCount(5);
        }
    }
}
