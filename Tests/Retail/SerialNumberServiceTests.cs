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
    public class SerialNumberServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly SerialNumberService _svc;

        public SerialNumberServiceTests()
        {
            _db = RetailTestDb.Create();
            RetailTestDb.SeedCatalog(_db);
            _svc = new SerialNumberService(_db, new RetailTestUser(), NullLogger<SerialNumberService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Settings_upsert_normalises_prefix_and_lists_only_items_that_require_serials()
        {
            var r = await _svc.SetSettingAsync(10, new SerialSettingUpsertDto(true, " slv "));
            r.Success.Should().BeTrue(r.Error);
            r.Data!.Prefix.Should().Be("SLV");
            r.Data.RequiresSerial.Should().BeTrue();
            (await _svc.SetSettingAsync(9, new SerialSettingUpsertDto(false, null))).Success.Should().BeTrue();
            (await _svc.SetSettingAsync(404, new SerialSettingUpsertDto(true, null))).Success.Should().BeFalse();
            (await _svc.SetSettingAsync(9, new SerialSettingUpsertDto(true, "A B"))).Success.Should().BeFalse("prefix with a space");

            var list = await _svc.ListSettingsAsync();
            list.Select(s => s.ItemId).Should().Equal(10);
            (await _svc.GetSettingAsync(9))!.RequiresSerial.Should().BeFalse();
            (await _svc.GetSettingAsync(404)).Should().BeNull();
        }

        [Fact]
        public async Task Receive_accepts_an_explicit_list_skips_duplicates_or_generates_with_the_prefix()
        {
            await _svc.SetSettingAsync(10, new SerialSettingUpsertDto(true, "SLV"));

            var list = await _svc.ReceiveAsync(new SerialReceiveDto(10, 1, new() { " abc-1 ", "ABC-2", "abc-1", "" }, null, "box 7"));
            list.Success.Should().BeTrue(list.Error);
            list.Data!.Created.Should().Be(2);
            list.Data.CreatedSerials.Should().Equal("ABC-1", "ABC-2");

            var again = await _svc.ReceiveAsync(new SerialReceiveDto(10, 1, new() { "ABC-2", "ABC-3" }, null, null));
            again.Data!.Created.Should().Be(1);
            again.Data.Skipped.Should().Equal("ABC-2");

            var gen = await _svc.ReceiveAsync(new SerialReceiveDto(10, null, null, 3, null));
            gen.Success.Should().BeTrue(gen.Error);
            gen.Data!.CreatedSerials.Should().Equal("SLV-0010-00001", "SLV-0010-00002", "SLV-0010-00003");
            var more = await _svc.ReceiveAsync(new SerialReceiveDto(10, null, null, 1, null));
            more.Data!.CreatedSerials.Should().Equal(new[] { "SLV-0010-00004" }, "numbering continues from the highest existing");

            var noPrefix = await _svc.ReceiveAsync(new SerialReceiveDto(9, null, null, 1, null));
            noPrefix.Data!.CreatedSerials.Should().Equal("SN-0009-00001");

            (await _svc.ReceiveAsync(new SerialReceiveDto(10, null, null, null, null))).Success.Should().BeFalse();
            (await _svc.ReceiveAsync(new SerialReceiveDto(10, 99, null, 1, null))).Success.Should().BeFalse("variant 99 is not an option of item 10");
            (await _svc.ReceiveAsync(new SerialReceiveDto(404, null, null, 1, null))).Success.Should().BeFalse();

            var unit = (await _svc.ListAsync(10, "InStock", "abc", null)).Should().HaveCount(3).And.Subject.Single(u => u.Serial == "ABC-1");
            unit.VariantName.Should().Be("Black");
            unit.BranchId.Should().Be(1);
            unit.Notes.Should().Be("box 7");
        }

        [Fact]
        public async Task Sell_marks_sold_on_the_invoice_is_idempotent_and_refuses_any_other_status()
        {
            await _svc.ReceiveAsync(new SerialReceiveDto(10, null, new() { "S1", "S2", "S3" }, null, null));

            var sold = await _svc.SellAsync(new SerialSellDto(" s1 ", 70));
            sold.Success.Should().BeTrue(sold.Error);
            sold.Data!.Status.Should().Be("Sold");
            sold.Data.TransactionId.Should().Be(70);
            sold.Data.SoldOn.Should().NotBeNull();

            var again = await _svc.SellAsync(new SerialSellDto("S1", 70));
            again.Success.Should().BeTrue("same serial on the same invoice is a no-op");
            again.Message.Should().Contain("already");

            var other = await _svc.SellAsync(new SerialSellDto("S1", 71));
            other.Success.Should().BeFalse();
            other.Error.Should().Contain("#70");

            (await _svc.SellAsync(new SerialSellDto("S2", 999))).Success.Should().BeFalse("invoice must exist");
            (await _svc.SellAsync(new SerialSellDto("UNKNOWN", 70))).Success.Should().BeFalse();

            // Faulty and Returned units cannot be sold.
            var s2 = (await _svc.ListAsync(10, null, "S2", null)).Single();
            (await _svc.MarkFaultyAsync(s2.Id, new SerialStatusChangeDto("torn"))).Success.Should().BeTrue();
            var faulty = await _svc.SellAsync(new SerialSellDto("S2", 70));
            faulty.Success.Should().BeFalse();
            faulty.Error.Should().Contain("Faulty");

            var ret = await _svc.ReturnAsync(sold.Data.Id, new SerialStatusChangeDto("customer changed mind"));
            ret.Success.Should().BeTrue(ret.Error);
            ret.Data!.Status.Should().Be("Returned");
            ret.Data.ReturnedOn.Should().NotBeNull();
            ret.Data.TransactionId.Should().Be(70, "the invoice stays for the audit trail");
            (await _svc.SellAsync(new SerialSellDto("S1", 71))).Success.Should().BeFalse("Returned must be restocked first");

            (await _svc.ListAsync(null, null, null, 70)).Should().ContainSingle(s => s.Serial == "S1");
        }

        [Fact]
        public async Task Return_faulty_and_restock_follow_the_status_machine()
        {
            await _svc.ReceiveAsync(new SerialReceiveDto(9, null, new() { "B1" }, null, null));
            var b1 = (await _svc.ListAsync(9, null, null, null)).Single();

            (await _svc.ReturnAsync(b1.Id, null)).Success.Should().BeFalse("only Sold can be returned");
            (await _svc.RestockAsync(b1.Id, null)).Success.Should().BeTrue("already in stock → no-op success");

            await _svc.SellAsync(new SerialSellDto("B1", 70));
            (await _svc.MarkFaultyAsync(b1.Id, null)).Success.Should().BeFalse("sold units must be returned first");
            (await _svc.RestockAsync(b1.Id, null)).Success.Should().BeFalse();

            (await _svc.ReturnAsync(b1.Id, null)).Success.Should().BeTrue();
            var faulty = await _svc.MarkFaultyAsync(b1.Id, new SerialStatusChangeDto("scratched"));
            faulty.Data!.Status.Should().Be("Faulty");
            faulty.Data.Notes.Should().Be("Faulty: scratched");

            var back = await _svc.RestockAsync(b1.Id, new SerialStatusChangeDto("repaired"));
            back.Data!.Status.Should().Be("InStock");
            back.Data.Notes.Should().Be("Faulty: scratched\nRestocked: repaired");
            (await _svc.SellAsync(new SerialSellDto("B1", 71))).Success.Should().BeTrue("restocked unit sells again");
            (await _svc.ReturnAsync(404, null)).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Summary_counts_per_status_and_shows_the_variance_against_item_stock()
        {
            await _svc.SetSettingAsync(10, new SerialSettingUpsertDto(true, "SLV"));
            await _svc.SetSettingAsync(9, new SerialSettingUpsertDto(true, null));   // required but nothing received yet
            await _svc.ReceiveAsync(new SerialReceiveDto(10, null, new() { "S1", "S2", "S3", "S4" }, null, null));
            await _svc.SellAsync(new SerialSellDto("S1", 70));
            await _svc.SellAsync(new SerialSellDto("S2", 70));
            var s2 = (await _svc.ListAsync(10, null, "S2", null)).Single();
            await _svc.ReturnAsync(s2.Id, null);
            var s3 = (await _svc.ListAsync(10, null, "S3", null)).Single();
            await _svc.MarkFaultyAsync(s3.Id, null);

            var all = await _svc.SummaryAsync(null);
            all.Should().HaveCount(2);
            var sleeves = all.Single(s => s.ItemId == 10);
            sleeves.Should().BeEquivalentTo(new { InStock = 1, Sold = 1, Returned = 1, Faulty = 1, ItemQuantity = 25, Variance = -24, RequiresSerial = true, Prefix = "SLV" });
            var booster = all.Single(s => s.ItemId == 9);
            booster.Should().BeEquivalentTo(new { InStock = 0, Sold = 0, Returned = 0, Faulty = 0, ItemQuantity = 40, Variance = -40 });

            (await _svc.SummaryAsync(10)).Should().ContainSingle(s => s.ItemId == 10);
            (await _svc.SummaryAsync(11)).Should().BeEmpty();
        }
    }
}
