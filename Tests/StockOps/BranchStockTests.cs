using Application.Common;
using Application.DTOs;
using Application.Services.StockOps;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.StockOps
{
    /// <summary>
    /// Phase 3 branch model: per-branch default locations, isolation from
    /// Ingredient.QuantityOnHand, branch positions and receive-to-location.
    /// </summary>
    public class BranchStockTests : IDisposable
    {
        private sealed class BranchUser : ICurrentUserAccessor
        {
            public BranchUser(int? branchId) { BranchId = branchId; }
            public int? UserId => 1;
            public string UserName => "stock@axislb.com";
            public IReadOnlyList<string> Roles => new[] { "admin" };
            public int? BranchId { get; }
            public bool IsInRole(string role) => Roles.Contains(role);
        }

        private readonly ApplicationDbContext _db;
        private const int Bey = 1, Jnh = 2;

        public BranchStockTests()
        {
            _db = StockOpsTestDb.Create();
            _db.AddRange(
                new Branch { Id = Bey, Code = "BEY", Name = "AXIS Beirut", IsDefault = true },
                new Branch { Id = Jnh, Code = "JNH", Name = "AXIS Jounieh" },
                new StockLocation { Id = 1, Name = "Main", Code = "MAIN", IsDefault = true, IsActive = true },
                new StockLocation { Id = 2, Name = "Bar Store", Code = "BAR", IsActive = true },
                new Ingredient { Id = 1, Name = "Coke can", Unit = "pcs", QuantityOnHand = 18m, ReorderLevel = 48m, BuyPricePerUnit = 0.6m },
                new Ingredient { Id = 2, Name = "Beef patty", Unit = "pcs", QuantityOnHand = 38m, ReorderLevel = 50m, BuyPricePerUnit = 1.8m },
                new ReorderRule { Id = 1, IngredientId = 2, MinQty = 10m, TargetQty = 40m, IsActive = true });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private StockLocationService Locations(int? branch) => new(_db, new BranchUser(branch), NullLogger<StockLocationService>.Instance);
        private StockCountService Counts(int? branch) => new(_db, new BranchUser(branch), NullLogger<StockCountService>.Instance);
        private StockTransferService Transfers(int? branch) => new(_db, new BranchUser(branch), NullLogger<StockTransferService>.Instance);
        private StockBatchService Batches(int? branch) => new(_db, new BranchUser(branch), NullLogger<StockBatchService>.Instance);
        private BranchStockService BranchStock(int? branch) => new(_db, new BranchUser(branch), Transfers(branch), NullLogger<BranchStockService>.Instance);

        private async Task<decimal> OnHand(int ingredientId) => (await _db.Set<Ingredient>().AsNoTracking().SingleAsync(i => i.Id == ingredientId)).QuantityOnHand;

        // ── Default locations ───────────────────────────────────────────

        [Fact]
        public async Task Branch_default_location_is_created_lazily_and_global_default_is_untouched()
        {
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();

            jnh.IsDefault.Should().BeTrue();
            jnh.BranchId.Should().Be(Jnh);
            jnh.Name.Should().Be("Main – JNH");
            jnh.Code.Should().Be("MAIN-JNH");

            // Second call reuses it; no second default for the branch.
            (await Locations(Jnh).GetOrCreateDefaultAsync()).Id.Should().Be(jnh.Id);
            (await _db.Set<StockLocation>().CountAsync(l => l.IsDefault && l.BranchId == Jnh)).Should().Be(1);

            // Global default stays the shared "Main" (BranchId null) and is what callers without a header get.
            var global = await Locations(null).GetOrCreateDefaultAsync();
            global.Id.Should().Be(1);
            global.BranchId.Should().BeNull();
            (await _db.Set<StockLocation>().CountAsync(l => l.IsDefault && l.BranchId == null)).Should().Be(1);
        }

        [Fact]
        public async Task Location_list_is_scoped_to_own_branch_plus_shared_and_unscoped_without_header()
        {
            await Locations(Bey).GetOrCreateDefaultAsync();
            await Locations(Jnh).GetOrCreateDefaultAsync();
            (await Locations(Jnh).CreateAsync(new StockLocationUpsertDto("Jounieh Bar", "JNH-BAR"))).Success.Should().BeTrue();

            var jnh = await Locations(Jnh).ListAsync();
            jnh.Select(l => l.Code).Should().BeEquivalentTo(new[] { "MAIN", "BAR", "MAIN-JNH", "JNH-BAR" });
            jnh.Should().NotContain(l => l.Code == "MAIN-BEY");

            var bey = await Locations(Bey).ListAsync();
            bey.Select(l => l.Code).Should().BeEquivalentTo(new[] { "MAIN", "BAR", "MAIN-BEY" });

            var all = await Locations(null).ListAsync();
            all.Select(l => l.Code).Should().BeEquivalentTo(new[] { "MAIN", "BAR", "MAIN-BEY", "MAIN-JNH", "JNH-BAR" });
        }

        [Fact]
        public async Task Making_a_location_default_only_replaces_the_default_of_its_own_scope()
        {
            var jnhDefault = await Locations(Jnh).GetOrCreateDefaultAsync();
            var r = await Locations(Jnh).CreateAsync(new StockLocationUpsertDto("Jounieh Cellar", "JNH-CELLAR", IsDefault: true));
            r.Success.Should().BeTrue(r.Error);

            (await _db.Set<StockLocation>().SingleAsync(l => l.Id == jnhDefault.Id)).IsDefault.Should().BeFalse();
            (await _db.Set<StockLocation>().SingleAsync(l => l.Id == r.Data!.Id)).IsDefault.Should().BeTrue();
            (await _db.Set<StockLocation>().SingleAsync(l => l.Id == 1)).IsDefault.Should().BeTrue("the global default is a different scope");
        }

        // ── Isolation ───────────────────────────────────────────────────

        [Fact]
        public async Task Count_posted_at_branch_default_moves_location_stock_only_and_never_touches_the_ingredient()
        {
            var svc = Counts(Jnh);
            var draft = (await svc.CreateDraftAsync(new StockCountCreateDto(null, "jounieh opening count"))).Data!;
            draft.LocationName.Should().Be("Main – JNH");
            draft.Lines.Should().OnlyContain(l => l.ExpectedQty == 0m, "a fresh branch default holds nothing");

            var coke = draft.Lines.Single(l => l.IngredientId == 1);
            await svc.UpdateLinesAsync(draft.Id, new StockCountLinesUpdateDto(new() { new(coke.Id, 12m) }));
            var posted = await svc.PostAsync(draft.Id);
            posted.Success.Should().BeTrue(posted.Error);

            (await OnHand(1)).Should().Be(18m, "the branch default does not mirror the ingredient");
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(0);
            var ls = await _db.Set<LocationStock>().SingleAsync();
            ls.LocationId.Should().Be(draft.LocationId);
            ls.IngredientId.Should().Be(1);
            ls.Quantity.Should().Be(12m);
        }

        [Fact]
        public async Task Count_posted_at_global_default_still_mirrors_the_ingredient()
        {
            var svc = Counts(null);
            var draft = (await svc.CreateDraftAsync(new StockCountCreateDto(null, null))).Data!;
            draft.LocationId.Should().Be(1);
            var coke = draft.Lines.Single(l => l.IngredientId == 1);
            coke.ExpectedQty.Should().Be(18m);

            await svc.UpdateLinesAsync(draft.Id, new StockCountLinesUpdateDto(new() { new(coke.Id, 15m) }));
            (await svc.PostAsync(draft.Id)).Success.Should().BeTrue();

            (await OnHand(1)).Should().Be(15m);
            var m = await _db.Set<StockMovement>().SingleAsync();
            m.Quantity.Should().Be(-3m);
            m.BalanceAfter.Should().Be(15m);
            (await _db.Set<LocationStock>().CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Transfer_from_main_to_branch_default_lowers_ingredient_and_fills_branch_rows()
        {
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();
            var svc = Transfers(Jnh);
            var t = (await svc.CreateAsync(new StockTransferCreateDto(1, jnh.Id, "send cans to Jounieh", new() { new(1, 6m) }))).Data!;
            var posted = await svc.PostAsync(t.Id);
            posted.Success.Should().BeTrue(posted.Error);

            (await OnHand(1)).Should().Be(12m);
            var m = await _db.Set<StockMovement>().SingleAsync();
            m.Quantity.Should().Be(-6m);
            m.Notes.Should().StartWith("Transfer to Main – JNH");
            (await _db.Set<LocationStock>().SingleAsync(s => s.LocationId == jnh.Id)).Quantity.Should().Be(6m);

            // Sending it back: branch rows drain, ingredient rises again, no double-counting.
            var back = (await svc.CreateAsync(new StockTransferCreateDto(jnh.Id, 1, null, new() { new(1, 2m) }))).Data!;
            (await svc.PostAsync(back.Id)).Success.Should().BeTrue();
            (await OnHand(1)).Should().Be(14m);
            (await _db.Set<LocationStock>().SingleAsync(s => s.LocationId == jnh.Id)).Quantity.Should().Be(4m);
        }

        [Fact]
        public async Task Transfer_count_and_batch_lists_hide_other_branches_but_show_shared_rows()
        {
            var bey = await Locations(Bey).GetOrCreateDefaultAsync();
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();
            await Transfers(Bey).CreateAsync(new StockTransferCreateDto(1, bey.Id, "to BEY", new() { new(1, 1m) }));
            await Transfers(Jnh).CreateAsync(new StockTransferCreateDto(1, jnh.Id, "to JNH", new() { new(1, 1m) }));
            await Transfers(null).CreateAsync(new StockTransferCreateDto(1, 2, "shared", new() { new(1, 1m) }));

            (await Transfers(Jnh).ListAsync(null, null)).Data.Select(t => t.Notes).Should().BeEquivalentTo(new[] { "to JNH", "shared" });
            (await Transfers(Bey).ListAsync(null, null)).Data.Select(t => t.Notes).Should().BeEquivalentTo(new[] { "to BEY", "shared" });
            (await Transfers(null).ListAsync(null, null)).TotalCount.Should().Be(3);

            await Counts(Bey).CreateDraftAsync(new StockCountCreateDto(null, "bey"));
            await Counts(Jnh).CreateDraftAsync(new StockCountCreateDto(null, "jnh"));
            await Counts(null).CreateDraftAsync(new StockCountCreateDto(2, "shared"));
            (await Counts(Jnh).ListAsync(null, null)).Data.Select(c => c.Notes).Should().BeEquivalentTo(new[] { "jnh", "shared" });
            (await Counts(null).ListAsync(null, null)).TotalCount.Should().Be(3);

            await Batches(Bey).ReceiveAsync(new StockBatchCreateDto(1, 5m, null, LotNumber: "BEY"));
            await Batches(Jnh).ReceiveAsync(new StockBatchCreateDto(1, 5m, null, LotNumber: "JNH"));
            await Batches(null).ReceiveAsync(new StockBatchCreateDto(1, 5m, null, LotNumber: "SHARED"));
            (await Batches(Jnh).ListAsync(new StockBatchFilterDto())).Data.Select(b => b.LotNumber).Should().BeEquivalentTo(new[] { "JNH", "SHARED" });
            (await Batches(null).ListAsync(new StockBatchFilterDto())).TotalCount.Should().Be(3);
        }

        // ── Positions ───────────────────────────────────────────────────

        [Fact]
        public async Task Branch_position_sums_default_and_other_locations_and_flags_below_level()
        {
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();
            var cellar = (await Locations(Jnh).CreateAsync(new StockLocationUpsertDto("Jounieh Cellar", "JNH-CELLAR"))).Data!;
            _db.AddRange(
                new LocationStock { LocationId = jnh.Id, IngredientId = 1, Quantity = 30m },
                new LocationStock { LocationId = cellar.Id, IngredientId = 1, Quantity = 10m },
                new LocationStock { LocationId = jnh.Id, IngredientId = 2, Quantity = 25m },
                new LocationStock { LocationId = 2, IngredientId = 1, Quantity = 99m }); // shared Bar Store: not Jounieh's
            await _db.SaveChangesAsync();

            var pos = await BranchStock(null).GetBranchPositionAsync(Jnh);

            pos.BranchId.Should().Be(Jnh);
            pos.BranchCode.Should().Be("JNH");
            pos.DefaultLocationId.Should().Be(jnh.Id);
            pos.OtherLocations.Select(l => l.Code).Should().BeEquivalentTo(new[] { "JNH-CELLAR" });

            var coke = pos.Lines.Single(l => l.IngredientId == 1);
            coke.DefaultQty.Should().Be(30m);
            coke.OtherQty.Should().Be(10m);
            coke.TotalQty.Should().Be(40m);
            coke.ReorderLevel.Should().Be(48m);           // Ingredient.ReorderLevel (no rule)
            coke.BelowLevel.Should().BeTrue();             // 40 <= 48
            coke.Value.Should().Be(24.00m);                // 40 x 0.6

            var beef = pos.Lines.Single(l => l.IngredientId == 2);
            beef.TotalQty.Should().Be(25m);
            beef.ReorderLevel.Should().Be(10m);            // rule MinQty wins over Ingredient.ReorderLevel 50
            beef.BelowLevel.Should().BeFalse();
            beef.Value.Should().Be(45.00m);                // 25 x 1.8

            pos.BelowLevelCount.Should().Be(1);
            pos.TotalValue.Should().Be(69.00m);

            // The header is the fallback when no branchId is passed.
            (await BranchStock(Jnh).GetBranchPositionAsync(null)).BranchId.Should().Be(Jnh);
            // No header, no branch => shared scope: global Main mirrors the ingredient, Bar Store is "other".
            var shared = await BranchStock(null).GetBranchPositionAsync(null);
            shared.BranchId.Should().BeNull();
            shared.DefaultLocationId.Should().Be(1);
            shared.Lines.Single(l => l.IngredientId == 1).DefaultQty.Should().Be(18m);
            shared.Lines.Single(l => l.IngredientId == 1).OtherQty.Should().Be(99m);
        }

        [Fact]
        public async Task Positions_put_shared_scope_first_then_every_active_branch()
        {
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();
            _db.Add(new LocationStock { LocationId = jnh.Id, IngredientId = 1, Quantity = 6m });
            await _db.SaveChangesAsync();

            var all = await BranchStock(null).GetPositionsAsync();

            all.Columns.Select(c => c.BranchId).Should().Equal(new int?[] { null, Bey, Jnh });
            all.Columns.Select(c => c.Code).Should().Equal("MAIN", "BEY", "JNH");
            var coke = all.Rows.Single(r => r.IngredientId == 1);
            coke.Cells.Select(c => c.Qty).Should().Equal(18m, 0m, 6m);
            coke.TotalQty.Should().Be(24m);
            coke.TotalValue.Should().Be(14.40m);
            coke.Cells[2].BelowLevel.Should().BeTrue();
            all.TotalValue.Should().Be(14.40m + 38m * 1.8m);
            // The BEY default was created on the way (lazily), so every branch now has one.
            (await _db.Set<StockLocation>().CountAsync(l => l.IsDefault)).Should().Be(3);
        }

        [Fact]
        public async Task Reorder_suggestions_use_branch_quantities_when_a_branch_is_selected()
        {
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();
            _db.Add(new LocationStock { LocationId = jnh.Id, IngredientId = 2, Quantity = 4m }); // beef: rule min 10 => suggest 36
            await _db.SaveChangesAsync();

            var global = await new ReorderService(_db).GetSuggestionsAsync();
            global.Select(s => s.IngredientId).Should().BeEquivalentTo(new[] { 1 }, "beef has 38 on hand > rule min 10; coke 18 <= 48");

            var branch = await new ReorderService(_db, new BranchUser(Jnh)).GetSuggestionsAsync();
            branch.Select(s => s.IngredientId).Should().BeEquivalentTo(new[] { 1, 2 });
            branch.Single(s => s.IngredientId == 2).QuantityOnHand.Should().Be(4m);
            branch.Single(s => s.IngredientId == 2).SuggestedQty.Should().Be(36m);
            branch.Single(s => s.IngredientId == 1).QuantityOnHand.Should().Be(0m);
        }

        // ── Receive to location ─────────────────────────────────────────

        private async Task<Purchase> SeedPurchase(int id = 1)
        {
            var p = new Purchase { Id = id, PurchaseDate = DateTime.UtcNow, InvoiceNumber = $"INV-{id}", TotalCost = 7.2m, CreatedBy = "t" };
            p.Lines.Add(new PurchaseLine { IngredientId = 1, Quantity = 12m, UnitCost = 0.6m, LineTotal = 7.2m });
            p.Lines.Add(new PurchaseLine { IngredientId = 2, Quantity = 5m, UnitCost = 1.8m, LineTotal = 9m });
            _db.Add(p);
            // PurchaseService already bumped the ingredient when the purchase was created.
            await _db.SaveChangesAsync();
            return p;
        }

        [Fact]
        public async Task Receive_to_location_posts_a_PO_transfer_once_and_is_idempotent()
        {
            await SeedPurchase();
            var jnh = await Locations(Jnh).GetOrCreateDefaultAsync();
            var svc = BranchStock(Jnh);

            var first = await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, jnh.Id));
            first.Success.Should().BeTrue(first.Error);
            first.Data!.AlreadyReceived.Should().BeFalse();
            first.Data.Transfer.Reference.Should().Be("PO-1");
            first.Data.Transfer.Status.Should().Be("Posted");
            first.Data.Transfer.FromLocationId.Should().Be(1);
            first.Data.Transfer.ToLocationId.Should().Be(jnh.Id);
            first.Data.Transfer.Lines.Should().HaveCount(2);

            (await OnHand(1)).Should().Be(6m);     // 18 - 12
            (await OnHand(2)).Should().Be(33m);    // 38 - 5
            (await _db.Set<LocationStock>().SingleAsync(s => s.LocationId == jnh.Id && s.IngredientId == 1)).Quantity.Should().Be(12m);
            (await _db.Set<LocationStock>().SingleAsync(s => s.LocationId == jnh.Id && s.IngredientId == 2)).Quantity.Should().Be(5m);
            (await _db.Set<StockMovement>().CountAsync(m => m.ReferenceType == "StockTransfer")).Should().Be(2);

            var again = await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, jnh.Id));
            again.Success.Should().BeTrue();
            again.Data!.AlreadyReceived.Should().BeTrue();
            again.Data.Transfer.Id.Should().Be(first.Data.Transfer.Id);
            again.Message.Should().Contain("already received");

            // Even to a different location nothing moves twice.
            (await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, 2))).Data!.AlreadyReceived.Should().BeTrue();
            (await OnHand(1)).Should().Be(6m);
            (await _db.Set<StockTransfer>().CountAsync()).Should().Be(1);
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(2);

            // Transfer carries the caller's branch like any other transfer.
            (await _db.Set<StockTransfer>().SingleAsync()).BranchId.Should().Be(Jnh);
        }

        [Fact]
        public async Task Receive_to_location_rejects_bad_input_and_cancels_a_failed_attempt_so_it_can_be_retried()
        {
            await SeedPurchase();
            var svc = BranchStock(null);

            (await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(999, 2))).Success.Should().BeFalse();
            (await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, 999))).Success.Should().BeFalse();
            var toMain = await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, 1));
            toMain.Success.Should().BeFalse();
            toMain.Error.Should().Contain("main location");

            // Not enough at Main: 38 beef but purchase wants 5 — fine; make coke short instead.
            var coke = await _db.Set<Ingredient>().SingleAsync(i => i.Id == 1);
            coke.QuantityOnHand = 3m;
            await _db.SaveChangesAsync();

            var fail = await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, 2));
            fail.Success.Should().BeFalse();
            fail.Error.Should().Contain("Insufficient Coke can");
            var cancelled = await _db.Set<StockTransfer>().SingleAsync();
            cancelled.Reference.Should().Be("PO-1");
            cancelled.Status.Should().Be("Cancelled");
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(0);
            (await _db.Set<LocationStock>().CountAsync()).Should().Be(0);

            // Restock and retry: a new PO-1 transfer is posted; the cancelled one does not block it.
            coke.QuantityOnHand = 20m;
            await _db.SaveChangesAsync();
            var ok = await svc.ReceiveToLocationAsync(new ReceiveToLocationDto(1, 2));
            ok.Success.Should().BeTrue(ok.Error);
            ok.Data!.AlreadyReceived.Should().BeFalse();
            (await _db.Set<StockTransfer>().CountAsync(t => t.Reference == "PO-1" && t.Status == "Posted")).Should().Be(1);
            (await OnHand(1)).Should().Be(8m);
        }
    }
}
