using Application.DTOs;
using Application.Services.Retail;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Retail
{
    public class BarcodeServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly BarcodeService _svc;

        public BarcodeServiceTests()
        {
            _db = RetailTestDb.Create();
            RetailTestDb.SeedCatalog(_db);
            _svc = new BarcodeService(_db, new RetailTestUser(), NullLogger<BarcodeService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task First_code_becomes_primary_and_only_one_primary_per_item_or_variant()
        {
            var a = await _svc.CreateAsync(new BarcodeCreateDto(9, null, "1111", "CODE128"));
            a.Success.Should().BeTrue(a.Error);
            a.Data!.IsPrimary.Should().BeTrue("the first code of an item is primary by default");

            var b = await _svc.CreateAsync(new BarcodeCreateDto(9, null, "2222", "CODE128"));
            b.Data!.IsPrimary.Should().BeFalse();

            var c = await _svc.CreateAsync(new BarcodeCreateDto(9, null, "3333", "CODE128", IsPrimary: true));
            c.Data!.IsPrimary.Should().BeTrue();
            (await _db.Set<ItemBarcode>().CountAsync(x => x.ItemId == 9 && x.IsPrimary)).Should().Be(1);
            (await _db.Set<ItemBarcode>().SingleAsync(x => x.Code == "1111")).IsPrimary.Should().BeFalse();

            // Variant-level primary is independent from the item-level one.
            var v = await _svc.CreateAsync(new BarcodeCreateDto(10, 1, "4444", "CODE128"));
            v.Data!.IsPrimary.Should().BeTrue();
            var set = await _svc.SetPrimaryAsync(b.Data.Id);
            set.Data!.IsPrimary.Should().BeTrue();
            (await _db.Set<ItemBarcode>().CountAsync(x => x.ItemId == 9 && x.IsPrimary)).Should().Be(1);
            (await _db.Set<ItemBarcode>().CountAsync(x => x.ItemId == 10 && x.VariantId == 1 && x.IsPrimary)).Should().Be(1);
        }

        [Fact]
        public async Task Codes_are_unique_across_the_whole_catalog_and_ean13_must_check_out()
        {
            (await _svc.CreateAsync(new BarcodeCreateDto(9, null, "1111", null))).Success.Should().BeTrue();
            var dup = await _svc.CreateAsync(new BarcodeCreateDto(10, null, " 11 11 ", "OTHER"));
            dup.Success.Should().BeFalse();
            dup.Error.Should().Contain("already assigned");

            var badEan = await _svc.CreateAsync(new BarcodeCreateDto(9, null, "2000000090008", "EAN13"));
            badEan.Success.Should().BeFalse();
            var badSym = await _svc.CreateAsync(new BarcodeCreateDto(9, null, "x1", "UPC"));
            badSym.Success.Should().BeFalse();
            var wrongVariant = await _svc.CreateAsync(new BarcodeCreateDto(9, 1, "x2", null));
            wrongVariant.Success.Should().BeFalse("variant 1 belongs to item 10");
        }

        [Fact]
        public async Task Lookup_normalises_whitespace_and_resolves_variant_codes_before_item_codes()
        {
            // Same text on the item and on its Green option: the option must win.
            await _svc.CreateAsync(new BarcodeCreateDto(10, null, "SLEEVES", null));
            _db.Set<ItemBarcode>().Add(new ItemBarcode { ItemId = 10, VariantId = 2, Code = "SLEEVES-G", Symbology = "OTHER", IsPrimary = true });
            await _db.SaveChangesAsync();

            var item = await _svc.LookupAsync(" SLEE VES\n");
            item.Success.Should().BeTrue(item.Error);
            item.Data!.ItemId.Should().Be(10);
            item.Data.VariantId.Should().BeNull();
            item.Data.Price.Should().Be(6m);
            item.Data.Stock.Should().Be(25);

            var green = await _svc.LookupAsync("SLEEVES-G");
            green.Data!.VariantId.Should().Be(2);
            green.Data.VariantName.Should().Be("Green");
            green.Data.Price.Should().Be(6.5m, "item price plus the option delta");
            green.Data.Stock.Should().Be(10, "the option's own stock");

            // Variant SKU is a fallback when no barcode row matches.
            var bySku = await _svc.LookupAsync("SLV-BLK");
            bySku.Success.Should().BeTrue(bySku.Error);
            bySku.Data!.VariantId.Should().Be(1);

            (await _svc.LookupAsync("NOPE")).Success.Should().BeFalse();
            (await _svc.LookupAsync("   ")).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Lookup_reports_serial_requirement_and_in_stock_count_and_resolves_a_serial_scan()
        {
            await _svc.GenerateAsync(new BarcodeGenerateDto(9, null));
            _db.AddRange(
                new SerialSetting { ItemId = 9, RequiresSerial = true, Prefix = "BST" },
                new SerialNumber { ItemId = 9, Serial = "BST-1", Status = SerialStatus.InStock },
                new SerialNumber { ItemId = 9, Serial = "BST-2", Status = SerialStatus.InStock },
                new SerialNumber { ItemId = 9, Serial = "BST-3", Status = SerialStatus.Sold, TransactionId = 70 });
            await _db.SaveChangesAsync();

            var r = await _svc.LookupAsync("2000000090009");
            r.Success.Should().BeTrue(r.Error);
            r.Data!.RequiresSerial.Should().BeTrue();
            r.Data.SerialsInStock.Should().Be(2);
            r.Data.MatchedSerial.Should().BeNull();

            var serialScan = await _svc.LookupAsync("bst-2".ToUpperInvariant());
            serialScan.Success.Should().BeTrue(serialScan.Error);
            serialScan.Data!.ItemId.Should().Be(9);
            serialScan.Data.MatchedSerial.Should().Be("BST-2");

            var soldScan = await _svc.LookupAsync("BST-3");
            soldScan.Success.Should().BeFalse();
            soldScan.Error.Should().Contain("Sold");
        }

        [Fact]
        public async Task Lookup_refuses_disabled_items()
        {
            await _svc.CreateAsync(new BarcodeCreateDto(11, null, "OLD", null));
            var r = await _svc.LookupAsync("OLD");
            r.Success.Should().BeFalse();
            r.Error.Should().Contain("not enabled");
        }

        [Fact]
        public async Task Generate_creates_the_in_house_ean13_as_primary_when_none_and_is_idempotent()
        {
            var first = await _svc.GenerateAsync(new BarcodeGenerateDto(9, null));
            first.Success.Should().BeTrue(first.Error);
            first.Data!.Code.Should().Be("2000000090009");
            first.Data.Symbology.Should().Be("EAN13");
            first.Data.IsPrimary.Should().BeTrue();

            var again = await _svc.GenerateAsync(new BarcodeGenerateDto(9, null));
            again.Success.Should().BeTrue();
            again.Data!.Id.Should().Be(first.Data.Id);
            (await _db.Set<ItemBarcode>().CountAsync(b => b.ItemId == 9)).Should().Be(1);

            // An item that already has a primary keeps it; the generated code is secondary.
            await _svc.CreateAsync(new BarcodeCreateDto(10, 2, "GRN-SUPPLIER", null));
            var gen = await _svc.GenerateAsync(new BarcodeGenerateDto(10, 2));
            gen.Data!.IsPrimary.Should().BeFalse();
            gen.Data.Code.Should().Be(Ean13.Build(10, 2));

            (await _svc.GenerateAsync(new BarcodeGenerateDto(999, null))).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Labels_give_one_row_per_plain_item_and_per_active_variant_with_the_primary_code()
        {
            await _svc.GenerateAsync(new BarcodeGenerateDto(9, null));
            await _svc.GenerateAsync(new BarcodeGenerateDto(10, 1));
            // Green has no own code → falls back to the item-level code (none here) → null.
            var labels = await _svc.LabelsAsync(new[] { 9, 10, 10, 404 });
            labels.Should().HaveCount(3);
            labels[0].Should().BeEquivalentTo(new { ItemId = 9, VariantId = (int?)null, Code = "2000000090009", Price = 5m });
            labels[1].Should().BeEquivalentTo(new { ItemId = 10, VariantId = 1, VariantName = "Black", Code = Ean13.Build(10, 1), Price = 6m });
            labels[2].Should().BeEquivalentTo(new { ItemId = 10, VariantId = 2, Code = (string?)null, Price = 6.5m });
        }

        [Fact]
        public async Task Deleting_the_primary_promotes_the_oldest_remaining_code()
        {
            var a = await _svc.CreateAsync(new BarcodeCreateDto(9, null, "A1", null));
            await _svc.CreateAsync(new BarcodeCreateDto(9, null, "B2", null));
            await _svc.CreateAsync(new BarcodeCreateDto(9, null, "C3", null));
            (await _svc.DeleteAsync(a.Data!.Id)).Success.Should().BeTrue();
            var remaining = await _svc.ListAsync(9, null);
            remaining.Should().HaveCount(2);
            remaining.Single(b => b.IsPrimary).Code.Should().Be("B2");
        }
    }
}
