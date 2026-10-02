using Application.DTOs;
using Application.IServices;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Payments
{
    /// <summary>
    /// A card-paid website order: the settlement hook must leave the order
    /// Paid even when the till invoice cannot be built, and the invoice must
    /// be linked to the order BEFORE it is closed — that link is how the
    /// journal books the sale to 1050 instead of 1000 Cash.
    /// </summary>
    public class ShopMarkPaidTests : IDisposable
    {
        private const int OrderId = 9, PaymentId = 501;
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly ApplicationDbContext _db;
        private readonly Mock<ITransactionRecordService> _till = new();
        private readonly ShopService _shop;
        private readonly List<string> _linkedWhenClosed = new();
        private int _created;

        public ShopMarkPaidTests()
        {
            _db = NewContext();
            _db.OnlineOrders.Add(new OnlineOrder
            {
                Id = OrderId, Code = "WEB-9", UserId = 1, CustomerName = "Test Buyer", CustomerPhone = "70000000",
                PaymentMode = "Online", Status = "AwaitingPayment", Subtotal = 30m, Total = 30m,
                Lines = { new OnlineOrderLine { ItemId = 3, ItemName = "Energy drink", UnitPrice = 15m, Quantity = 2, LineTotal = 30m } },
            });
            _db.SaveChanges();

            _till.Setup(t => t.CreateCoffeeShopOrder(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()))
                .ReturnsAsync((int? u, int _, List<OrderItemRequest> _, string by, CancellationToken _, string _, bool open, int? _, int? _, decimal _) =>
                {
                    _created++;
                    var tx = new TransactionRecord { StatusId = open ? 7 : 6, TotalPrice = 30m, CreatedBy = by, UserId = u };
                    _db.Transactions.Add(tx);
                    _db.SaveChanges();
                    return new BaseResponse<TransactionDto>(true, null, "ok", Tx(tx.Id, tx.TotalPrice, tx.StatusId));
                });
            _till.Setup(t => t.CloseOpenInvoice(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()))
                .ReturnsAsync((int id, string _, CancellationToken _, decimal _) =>
                {
                    // What JournalService.ResolveSaleCashAccountAsync will look up at posting time.
                    using (var probe = NewContext())
                        _linkedWhenClosed.Add(probe.OnlineOrders.Where(o => o.TransactionRecordId == id).Select(o => o.PaymentMode).FirstOrDefault() ?? "(none)");
                    var tx = _db.Transactions.Single(t => t.Id == id);
                    tx.StatusId = 6;
                    _db.SaveChanges();
                    return new BaseResponse<TransactionDto>(true, null, "ok", Tx(id, tx.TotalPrice, 6));
                });

            var sp = new ServiceCollection().AddSingleton(_till.Object).BuildServiceProvider();
            _shop = new ShopService(
                new BaseRepository<OnlineOrder>(_db), new BaseRepository<OnlineOrderLine>(_db),
                new BaseRepository<Item>(_db), new BaseRepository<ItemAddOn>(_db), new BaseRepository<ItemVariant>(_db),
                new BaseRepository<Channel>(_db), new BaseRepository<TransactionRecord>(_db),
                users: null!, auth: null!, wallets: null!, payments: new Mock<IOnlinePaymentService>().Object,
                new BaseRepository<Category>(_db), new Mock<IShippingService>().Object, new Mock<IIntegrationSettingsService>().Object,
                new UnitOfWork(_db), sp, NullLogger<ShopService>.Instance);
        }

        private ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        public void Dispose() => _db.Dispose();

        private static TransactionDto Tx(int id, decimal total, int status) => new(
            id, null, "", null, "", null, "", null, "", 0m, total, status, DateTime.UtcNow, null, "test",
            new List<TransactionItemDto>(), null, "", null, null, null, 1, false, null, null, null);

        private OnlineOrder Persisted() { using var db = NewContext(); return db.OnlineOrders.AsNoTracking().Single(o => o.Id == OrderId); }

        [Fact]
        public async Task Settlement_marks_Paid_and_closes_an_invoice_that_was_linked_first()
        {
            await _shop.MarkPaidAsync(OrderId, PaymentId);

            var o = Persisted();
            o.Status.Should().Be("Paid");
            o.PaidOn.Should().NotBeNull();
            o.OnlinePaymentId.Should().Be(PaymentId);
            o.TransactionRecordId.Should().NotBeNull();
            _db.Transactions.AsNoTracking().Single(t => t.Id == o.TransactionRecordId).StatusId.Should().Be(6);
            _till.Verify(t => t.CreateCoffeeShopOrder(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>(), true, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()), Times.Once);
            _linkedWhenClosed.Should().Equal("Online");   // → JournalService books 1050
        }

        [Fact]
        public async Task Invoice_failure_still_leaves_the_order_Paid_and_retry_finishes_it()
        {
            _till.Setup(t => t.CreateCoffeeShopOrder(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()))
                .ReturnsAsync(new BaseResponse<TransactionDto>(false, "Invalid items", "The following item IDs do not exist: 3"));

            var act = () => _shop.MarkPaidAsync(OrderId, PaymentId);
            await act.Should().ThrowAsync<InvalidOperationException>();   // → OnlinePayment.FulfillmentError, Reconcile retries

            var o = Persisted();
            o.Status.Should().Be("Paid");                                 // the till sees a paid order
            o.TransactionRecordId.Should().BeNull();
        }

        [Fact]
        public async Task Invoice_created_but_not_closed_is_resumed_not_duplicated()
        {
            var closeCalls = 0;
            _till.Setup(t => t.CloseOpenInvoice(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()))
                .ReturnsAsync((int id, string _, CancellationToken _, decimal _) =>
                {
                    if (++closeCalls == 1) return new BaseResponse<TransactionDto>(false, "db error", "Failed to close invoice. Please try again.");
                    var tx = _db.Transactions.Single(t => t.Id == id); tx.StatusId = 6; _db.SaveChanges();
                    return new BaseResponse<TransactionDto>(true, null, "ok", Tx(id, 30m, 6));
                });

            await ((Func<Task>)(() => _shop.MarkPaidAsync(OrderId, PaymentId))).Should().ThrowAsync<InvalidOperationException>();
            Persisted().Status.Should().Be("Paid");

            await _shop.MarkPaidAsync(OrderId, PaymentId);                // Reconcile

            _created.Should().Be(1);
            var o = Persisted();
            _db.Transactions.AsNoTracking().Single(t => t.Id == o.TransactionRecordId).StatusId.Should().Be(6);
        }

        [Fact]
        public async Task Repeated_settlement_is_a_no_op()
        {
            await _shop.MarkPaidAsync(OrderId, PaymentId);
            await _shop.MarkPaidAsync(OrderId, PaymentId);

            _created.Should().Be(1);
            _till.Verify(t => t.CloseOpenInvoice(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()), Times.Once);
        }

        [Fact]
        public async Task Till_accepting_a_Paid_order_without_invoice_books_it_the_1050_way()
        {
            var o = _db.OnlineOrders.Single(x => x.Id == OrderId);
            o.Status = "Paid"; o.PaidOn = DateTime.UtcNow;   // settlement hook got this far, invoice failed
            _db.SaveChanges();

            var (ok, error, _) = await _shop.AcceptAsync(OrderId, "cashier");

            ok.Should().BeTrue(error);
            Persisted().Status.Should().Be("Accepted");
            _till.Verify(t => t.CreateCoffeeShopOrder(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>(), true, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()), Times.Once);
            _linkedWhenClosed.Should().Equal("Online");
        }
    }
}
