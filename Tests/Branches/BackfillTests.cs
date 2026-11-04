using Application.Services.Branches;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Branches
{
    /// <summary>POST api/branches/backfill: per-table stamping of legacy (BranchId null) rows.</summary>
    public class BackfillTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();

        public BackfillTests()
        {
            _db.AddRange(
                new Branch { Id = 1, Code = "BEY", Name = "Beirut", IsDefault = true },
                new Branch { Id = 2, Code = "JNH", Name = "Jounieh" });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private BranchService Svc() => new(_db, new FakeUser { Roles = new[] { "admin" } }, NullLogger<BranchService>.Instance);

        private void SeedLegacyRows()
        {
            var now = DateTime.UtcNow;
            _db.AddRange(
                new TransactionRecord { Id = 1, StatusId = 6, TotalPrice = 10, CreatedBy = "t", BranchId = null },
                new TransactionRecord { Id = 2, StatusId = 6, TotalPrice = 10, CreatedBy = "t", BranchId = 2 },
                new CashierShift { Id = 1, RegisterName = "R1", UserName = "c", BranchId = null },
                new CashierShift { Id = 2, RegisterName = "R1", UserName = "c", BranchId = null },
                new CashierShift { Id = 3, RegisterName = "R2", UserName = "c", BranchId = 2 },
                new PaymentTender { Id = 1, CurrencyCode = "USD", CreatedBy = "c", BranchId = null },
                new DeliveryOrder { Id = 1, CustomerName = "A", BranchId = null },
                new Reservation { Id = 1, CustomerName = "B", Phone = "1", CreatedBy = "c", StartOn = now, EndOn = now.AddHours(1), BranchId = null },
                new Tip { Id = 1, CreatedBy = "c", BranchId = null },
                new AttendanceRecord { Id = 1, ClockIn = now, BranchId = null },
                new StockCount { Id = 1, LocationId = 1, Reference = "SC-00001", BranchId = null },
                new StockTransfer { Id = 1, FromLocationId = 1, ToLocationId = 2, Reference = "TR-00001", BranchId = null },
                new StockBatch { Id = 1, IngredientId = 1, Quantity = 1, RemainingQuantity = 1, BranchId = null },
                new Combo { Id = 1, Name = "Meal", BranchId = null },
                new ComboApplication { Id = 1, ComboId = 1, TransactionId = 1, Quantity = 1, BranchId = null },
                new Coupon { Id = 1, Code = "C1", Name = "Coupon", BranchId = null },
                new GiftCard { Id = 1, Code = "GC1", BranchId = null },
                new HappyHourRule { Id = 1, Name = "HH", BranchId = null },
                // Locations are NOT part of the backfill: null there means "shared".
                new StockLocation { Id = 1, Name = "Main", Code = "MAIN", IsDefault = true, BranchId = null },
                new StockLocation { Id = 2, Name = "Bar", Code = "BAR", BranchId = null });
            _db.SaveChanges();
        }

        [Fact]
        public async Task Backfill_all_stamps_every_null_row_per_table_and_reports_counts()
        {
            SeedLegacyRows();

            var r = await Svc().BackfillAsync(null, "all");

            r.Success.Should().BeTrue(r.Error);
            r.Data!.BranchId.Should().Be(1);
            r.Data.BranchCode.Should().Be("BEY");
            r.Data.Tables.Should().Equal(BranchService.BackfillTables);
            r.Data.Counts.Should().Equal(new Dictionary<string, int>
            {
                ["transactions"] = 1,
                ["Till_CashierShifts"] = 2,
                ["Till_PaymentTenders"] = 1,
                ["Delivery_Orders"] = 1,
                ["Floor_Reservations"] = 1,
                ["Floor_Tips"] = 1,
                ["Hr_AttendanceRecords"] = 1,
                ["StockOps_StockCounts"] = 1,
                ["StockOps_StockTransfers"] = 1,
                ["StockOps_StockBatches"] = 1,
                ["Promo_Combos"] = 1,
                ["Promo_ComboApplications"] = 1,
                ["Promo_Coupons"] = 1,
                ["Promo_GiftCards"] = 1,
                ["Promo_HappyHourRules"] = 1,
            });
            r.Data.Total.Should().Be(16);

            (await _db.Set<CashierShift>().CountAsync(s => s.BranchId == null)).Should().Be(0);
            (await _db.Set<CashierShift>().SingleAsync(s => s.Id == 3)).BranchId.Should().Be(2, "rows that already carry a branch are left alone");
            (await _db.Set<TransactionRecord>().SingleAsync(t => t.Id == 2)).BranchId.Should().Be(2);
            (await _db.Set<GiftCard>().SingleAsync()).BranchId.Should().Be(1);
            (await _db.Set<StockLocation>().CountAsync(l => l.BranchId == null)).Should().Be(2, "locations are never backfilled");

            // Idempotent: a second run finds nothing.
            var again = await Svc().BackfillAsync(null, null);
            again.Data!.Total.Should().Be(0);
            again.Data.Counts.Values.Should().OnlyContain(c => c == 0);
        }

        [Fact]
        public async Task Backfill_accepts_a_subset_of_tables_and_an_explicit_branch()
        {
            SeedLegacyRows();

            var r = await Svc().BackfillAsync(2, "Till_CashierShifts, till_paymenttenders");

            r.Success.Should().BeTrue(r.Error);
            r.Data!.BranchId.Should().Be(2);
            r.Data.Tables.Should().Equal("Till_CashierShifts", "Till_PaymentTenders");
            r.Data.Counts.Should().HaveCount(2);
            r.Data.Counts["Till_CashierShifts"].Should().Be(2);
            r.Data.Counts["Till_PaymentTenders"].Should().Be(1);
            r.Data.Total.Should().Be(3);
            (await _db.Set<CashierShift>().CountAsync(s => s.BranchId == 2)).Should().Be(3);
            (await _db.Set<TransactionRecord>().SingleAsync(t => t.Id == 1)).BranchId.Should().BeNull("transactions were not selected");
            (await _db.Set<Tip>().SingleAsync()).BranchId.Should().BeNull();
        }

        [Fact]
        public async Task Backfill_rejects_unknown_tables_and_branches_without_touching_anything()
        {
            SeedLegacyRows();

            var badTable = await Svc().BackfillAsync(null, "Till_CashierShifts,StockOps_Locations");
            badTable.Success.Should().BeFalse();
            badTable.Error.Should().Contain("StockOps_Locations");

            var badBranch = await Svc().BackfillAsync(999, "all");
            badBranch.Success.Should().BeFalse();
            badBranch.Message.Should().Be("Not found");

            (await _db.Set<CashierShift>().CountAsync(s => s.BranchId == null)).Should().Be(2);
        }

        [Fact]
        public async Task Legacy_backfill_transactions_endpoint_still_behaves_as_before()
        {
            SeedLegacyRows();

            var r = await Svc().BackfillTransactionsAsync(null);

            r.Success.Should().BeTrue();
            r.Data.Should().Be(1);
            (await _db.Set<TransactionRecord>().SingleAsync(t => t.Id == 1)).BranchId.Should().Be(1);
            (await _db.Set<CashierShift>().CountAsync(s => s.BranchId == null)).Should().Be(2, "the old endpoint only touches transactions");
        }
    }
}
