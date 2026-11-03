using Application.DTOs;
using Application.IServices;
using Application.Services.OrderingChannels;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.OrderingChannels.OrderingChannelsTestSupport;

namespace Tests.OrderingChannels
{
    /// <summary>QR self-ordering: table validation, session lifecycle, first round opens the invoice, later rounds append.</summary>
    public class SelfOrderSessionTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly SelfOrderService _svc;
        private readonly Mock<ITransactionRecordService> _trx;
        private readonly List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)> _calls = new();
        private readonly List<(int invoiceId, List<OrderItemRequest> items)> _appends = new();
        private int _invoiceStatus = 7;

        public SelfOrderSessionTests()
        {
            _db = NewDb();
            SeedMenu(_db);
            _db.Add(new SelfOrderTable { Id = 1, Code = "T1", Label = "Table 1", IsActive = true });
            _db.Add(new SelfOrderTable { Id = 2, Code = "T2", Label = "Table 2 (closed)", IsActive = false });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            _trx = TrxService(_db, _calls);
            _trx.Setup(s => s.AddItemsToOpenInvoice(It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((int invoiceId, List<OrderItemRequest> items, string by, CancellationToken ct) =>
                {
                    _appends.Add((invoiceId, items));
                    return Task.FromResult(new BaseResponse<TransactionDto>(true, null, "ok",
                        Trx(invoiceId, 20m, items: new List<TransactionItemDto> { Line(1, "Latte", 1, 5m), Line(3, "Burger", 1, 12m), Line(2, "Croissant", 1, 3m) })));
                });
            _trx.Setup(s => s.GetWithItemsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((int id, CancellationToken ct) => Task.FromResult<TransactionDto?>(
                    Trx(id, 17m, _invoiceStatus, items: new List<TransactionItemDto> { Line(1, "Latte", 1, 5m), Line(3, "Burger", 1, 12m) })));

            var menu = new PublicMenuBuilder(new Mock<IMenuService>().Object, _db);
            _svc = new SelfOrderService(_db, _trx.Object, User(userId: null, name: "system").Object,
                new Mock<IIntegrationSettingsService>().Object, menu, NullLogger<SelfOrderService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Inactive_or_unknown_table_cannot_start_a_session()
        {
            (await _svc.StartSessionAsync("T2")).Error.Should().Be("NotFound");
            (await _svc.StartSessionAsync("NOPE")).Error.Should().Be("NotFound");
            (await _svc.GetMenuAsync("T2")).Success.Should().BeFalse();
            _db.Set<SelfOrderSession>().Should().BeEmpty();
        }

        [Fact]
        public async Task Session_lifecycle_open_submit_append_then_closed_when_paid()
        {
            var start = await _svc.StartSessionAsync("t1");   // code is case-insensitive
            start.Success.Should().BeTrue(start.Message);
            var token = start.Data!.Token;
            start.Data.Status.Should().Be("Open");
            start.Data.TableLabel.Should().Be("Table 1");

            // Empty cart is refused and nothing is created.
            (await _svc.SubmitAsync(token, new SelfOrderSubmitDto(new List<OrderItemRequest>()))).Success.Should().BeFalse();
            _calls.Should().BeEmpty();

            // First round opens the invoice.
            var first = await _svc.SubmitAsync(token, new SelfOrderSubmitDto(new List<OrderItemRequest> { new(1, 1), new(3, 1) }, "extra napkins"));
            first.Success.Should().BeTrue(first.Message);
            first.Data!.Status.Should().Be("Submitted");
            first.Data.TransactionId.Should().Be(1000);
            first.Data.Total.Should().Be(17m);
            _calls.Should().ContainSingle();
            _calls[0].open.Should().BeTrue();
            _calls[0].comment.Should().Be("Self-order table T1 | extra napkins");
            _appends.Should().BeEmpty();

            // Second round appends to the same open invoice, never creates a new one.
            var second = await _svc.SubmitAsync(token, new SelfOrderSubmitDto(new List<OrderItemRequest> { new(2, 1) }));
            second.Success.Should().BeTrue(second.Message);
            second.Data!.TransactionId.Should().Be(1000);
            second.Data.Items.Should().HaveCount(3);
            _calls.Should().ContainSingle();
            _appends.Should().ContainSingle(a => a.invoiceId == 1000);

            // Status while open.
            var status = await _svc.GetStatusAsync(token);
            status.Data!.Status.Should().Be("Submitted");
            status.Data.InvoicePaid.Should().BeFalse();
            status.Data.Items.Select(i => i.ItemName).Should().Equal("Latte", "Burger");
            status.Data.GuestNote.Should().Be("extra napkins");

            // Till closes the invoice → session closes and refuses further rounds.
            _invoiceStatus = 6;
            var paid = await _svc.GetStatusAsync(token);
            paid.Data!.InvoicePaid.Should().BeTrue();
            paid.Data.Status.Should().Be("Closed");
            var late = await _svc.SubmitAsync(token, new SelfOrderSubmitDto(new List<OrderItemRequest> { new(1, 1) }));
            late.Success.Should().BeFalse();
            late.Error.Should().Be("Closed");
        }

        [Fact]
        public async Task Unknown_token_is_rejected()
        {
            (await _svc.GetStatusAsync(Guid.NewGuid())).Error.Should().Be("NotFound");
            (await _svc.SubmitAsync(Guid.NewGuid(), new SelfOrderSubmitDto(new List<OrderItemRequest> { new(1, 1) }))).Error.Should().Be("NotFound");
        }

        [Fact]
        public async Task Table_admin_normalises_codes_and_keeps_them_unique_and_builds_qr_urls()
        {
            var created = await _svc.CreateTableAsync(new SelfOrderTableUpsertDto(" t3 ", "Terrace 3"));
            created.Success.Should().BeTrue(created.Message);
            created.Data!.Code.Should().Be("T3");

            (await _svc.CreateTableAsync(new SelfOrderTableUpsertDto("t1", "Dup"))).Success.Should().BeFalse();
            (await _svc.CreateTableAsync(new SelfOrderTableUpsertDto("bad code!", "x"))).Success.Should().BeFalse();

            var qrs = await _svc.ListTableQrsAsync("https://app.axislb.com/");
            qrs.Should().Contain(q => q.Code == "T3" && q.PublicUrl == "https://app.axislb.com/order/T3" && q.QrPayload == q.PublicUrl);
        }
    }
}
