using System.Text.Json;
using Application.DTOs;
using Application.Services.Offline;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Application.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Offline
{
    /// <summary>
    /// Replay of a till's offline queue: each op runs once, a replayed opId
    /// returns the stored response, one bad op never sinks the batch, and a
    /// reused opId with a different payload is reported as a conflict.
    /// </summary>
    public class OfflineSyncTests : IDisposable
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly Mock<ITransactionRecordService> _tx = new(MockBehavior.Strict);
        private readonly OfflineSyncService _svc;

        public OfflineSyncTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            _svc = new OfflineSyncService(
                new BaseRepository<IdempotencyRecord>(_db),
                new BaseRepository<OfflineSyncBatch>(_db),
                new UnitOfWork(_db),
                _tx.Object,
                NullLogger<OfflineSyncService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        // ── helpers ──────────────────────────────────────────────────────

        private static TransactionDto Tx(int id, int statusId = 6, decimal total = 10m) => new(
            id, null, "", null, "", null, "", null, "", 0, total, statusId,
            DateTime.UtcNow, null, "cashier@axis", new List<TransactionItemDto>(),
            null, "", null, null, null, 1, false, null, null, null);

        private static BaseResponse<TransactionDto> Ok(TransactionDto dto) => new(true, null, "ok", dto);
        private static BaseResponse<TransactionDto> Fail(string err) => new(false, err, err, null);

        private static OfflineOpRequest Op(Guid id, string type, object payload)
            => new(id, type, JsonSerializer.SerializeToElement(payload, Web), DateTime.UtcNow);

        private static object CreatePayload(int itemId = 1, int qty = 2, bool open = false) => new
        {
            userId = (int?)null,
            itemsRequest = new[] { new { itemId, quantity = qty } },
            discountId = 0,
            isOpenInvoice = open,
            setId = (int?)null,
            comment = "table 4",
        };

        private Task<OfflineSyncResponse> Sync(params OfflineOpRequest[] ops)
            => _svc.SyncAsync(new OfflineSyncRequest("till-1", ops.ToList()), "cashier@axis", 3);

        private static object GameCreatePayload(int gameId = 1, int settingId = 1, int hours = 1, int setId = 1, int persons = 1, int discountId = 0) => new
        {
            gameId,
            gameSettingId = settingId,
            hours,
            status = 1,
            setId,
            discountId,
            userId = (int?)null,
            numberOfPersons = persons,
            isDayPass = false,
            comment = "offline start",
        };

        private void GameCreateReturns(int id, int statusId = 7) =>
            _tx.Setup(t => t.CreateGameSession(
                    It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), "cashier@axis",
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(Ok(Tx(id, statusId)));

        private void CreateReturns(int id, bool open = false) =>
            _tx.Setup(t => t.CreateCoffeeShopOrder(
                    It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), "cashier@axis",
                    It.IsAny<CancellationToken>(), It.IsAny<string>(), open, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()))
                .ReturnsAsync(Ok(Tx(id, open ? 7 : 6)));

        // ── tests ────────────────────────────────────────────────────────

        [Fact]
        public async Task First_sync_executes_the_order_and_stores_an_idempotency_record()
        {
            CreateReturns(101);
            var opId = Guid.NewGuid();

            var r = await Sync(Op(opId, OfflineOpTypes.CreateCoffeeShopOrder, CreatePayload()));

            r.Results.Should().ContainSingle();
            var one = r.Results[0];
            one.Outcome.Should().Be(OfflineOpOutcomes.Ok);
            one.Status.Should().Be(200);
            one.Result!.Value.GetProperty("data").GetProperty("id").GetInt32().Should().Be(101);

            var rec = _db.Set<IdempotencyRecord>().Single();
            rec.Key.Should().Be(opId.ToString("D"));
            rec.Endpoint.Should().Be(OfflineOpTypes.CreateCoffeeShopOrder);
            rec.CreatedBy.Should().Be("cashier@axis");
            rec.BranchId.Should().Be(3);
            rec.ResponseStatus.Should().Be(200);
        }

        [Fact]
        public async Task Replaying_the_same_op_returns_the_stored_response_without_re_executing()
        {
            CreateReturns(101);
            var op = Op(Guid.NewGuid(), OfflineOpTypes.CreateCoffeeShopOrder, CreatePayload());

            var first = await Sync(op);
            var second = await Sync(op);

            second.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Replayed);
            second.Results[0].Status.Should().Be(200);
            second.Results[0].Result!.Value.GetRawText().Should().Be(first.Results[0].Result!.Value.GetRawText());

            _tx.Verify(t => t.CreateCoffeeShopOrder(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()),
                Times.Once);
            _db.Set<IdempotencyRecord>().Count().Should().Be(1);
            _db.Set<OfflineSyncBatch>().Count().Should().Be(2);
        }

        [Fact]
        public async Task Same_key_with_a_different_payload_is_a_409_conflict_and_is_not_executed()
        {
            CreateReturns(101);
            var opId = Guid.NewGuid();
            await Sync(Op(opId, OfflineOpTypes.CreateCoffeeShopOrder, CreatePayload(itemId: 1, qty: 2)));

            var r = await Sync(Op(opId, OfflineOpTypes.CreateCoffeeShopOrder, CreatePayload(itemId: 1, qty: 3)));

            r.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Conflict);
            r.Results[0].Status.Should().Be(409);
            r.Results[0].Error.Should().NotBeNullOrEmpty();
            _tx.Verify(t => t.CreateCoffeeShopOrder(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()),
                Times.Once);
        }

        [Fact]
        public async Task One_failing_op_does_not_stop_the_others_and_failed_ops_are_not_stored()
        {
            CreateReturns(101);
            _tx.Setup(t => t.CloseOpenInvoice(55, "cashier@axis", It.IsAny<CancellationToken>(), 0m))
                .ThrowsAsync(new InvalidOperationException("db hiccup"));
            _tx.Setup(t => t.AddItemsToOpenInvoice(77, It.IsAny<List<OrderItemRequest>>(), "cashier@axis", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Fail("Invoice is closed"));

            var r = await Sync(
                Op(Guid.NewGuid(), OfflineOpTypes.CloseOpenInvoice, new { invoiceId = 55 }),
                Op(Guid.NewGuid(), OfflineOpTypes.AddItemsToOpenInvoice, new { invoiceId = 77, itemsRequest = new[] { new { itemId = 1, quantity = 1 } } }),
                Op(Guid.NewGuid(), "DeleteEverything", new { }),
                Op(Guid.NewGuid(), OfflineOpTypes.CreateCoffeeShopOrder, CreatePayload()));

            r.Results.Should().HaveCount(4);
            r.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            r.Results[0].Status.Should().Be(500);
            r.Results[0].Error.Should().Contain("db hiccup");
            r.Results[1].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            r.Results[1].Status.Should().Be(400);
            r.Results[1].Error.Should().Be("Invoice is closed");
            r.Results[2].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            r.Results[2].Error.Should().Contain("Unknown op type");
            r.Results[3].Outcome.Should().Be(OfflineOpOutcomes.Ok);

            // only the success is remembered; failures stay retryable
            _db.Set<IdempotencyRecord>().Count().Should().Be(1);

            var batch = _db.Set<OfflineSyncBatch>().Single();
            batch.ClientId.Should().Be("till-1");
            batch.ReceivedBy.Should().Be("cashier@axis");
            batch.OpsCount.Should().Be(4);
            batch.SucceededCount.Should().Be(1);
            batch.FailedCount.Should().Be(3);
        }

        [Fact]
        public async Task A_failed_op_is_executed_again_on_the_next_sync()
        {
            var calls = 0;
            _tx.Setup(t => t.CloseOpenInvoice(55, "cashier@axis", It.IsAny<CancellationToken>(), 0m))
                .ReturnsAsync(() => ++calls == 1 ? Fail("temporarily locked") : Ok(Tx(55)));
            var op = Op(Guid.NewGuid(), OfflineOpTypes.CloseOpenInvoice, new { invoiceId = 55 });

            (await Sync(op)).Results[0].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            (await Sync(op)).Results[0].Outcome.Should().Be(OfflineOpOutcomes.Ok);
            (await Sync(op)).Results[0].Outcome.Should().Be(OfflineOpOutcomes.Replayed);

            calls.Should().Be(2);
        }

        [Fact]
        public async Task Follow_up_ops_on_an_offline_created_invoice_resolve_the_real_id_by_invoiceOpId()
        {
            CreateReturns(500, open: true);
            _tx.Setup(t => t.AddItemsToOpenInvoice(500, It.IsAny<List<OrderItemRequest>>(), "cashier@axis", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ok(Tx(500, 7)));
            _tx.Setup(t => t.CloseOpenInvoice(500, "cashier@axis", It.IsAny<CancellationToken>(), 5m))
                .ReturnsAsync(Ok(Tx(500, 6)));

            var createOp = Guid.NewGuid();
            // create + add in one batch, close in a later batch (resolved from the stored record)
            var r1 = await Sync(
                Op(createOp, OfflineOpTypes.CreateCoffeeShopOrder, CreatePayload(open: true)),
                Op(Guid.NewGuid(), OfflineOpTypes.AddItemsToOpenInvoice, new { invoiceId = -1, invoiceOpId = createOp, itemsRequest = new[] { new { itemId = 2, quantity = 1 } } }));
            var r2 = await Sync(
                Op(Guid.NewGuid(), OfflineOpTypes.CloseOpenInvoice, new { invoiceId = -1, invoiceOpId = createOp, walletAmount = 5 }));

            r1.Results.Select(x => x.Outcome).Should().AllBe(OfflineOpOutcomes.Ok);
            r2.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Ok);
            _tx.VerifyAll();
        }

        [Fact]
        public async Task A_negative_invoice_id_without_a_known_create_op_fails_only_that_op()
        {
            var r = await Sync(Op(Guid.NewGuid(), OfflineOpTypes.CloseOpenInvoice, new { invoiceId = -9, invoiceOpId = Guid.NewGuid() }));

            r.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            r.Results[0].Status.Should().Be(400);
            r.Results[0].Error.Should().Contain("not been created");
        }

        [Fact]
        public async Task Invalid_payload_is_a_400_and_nothing_is_executed()
        {
            var r = await Sync(Op(Guid.NewGuid(), OfflineOpTypes.CreateCoffeeShopOrder, new { itemsRequest = Array.Empty<object>() }));

            r.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            r.Results[0].Status.Should().Be(400);
            _db.Set<IdempotencyRecord>().Should().BeEmpty();
        }

        // ── game sessions (phase 3) ──────────────────────────────────────

        [Fact]
        public async Task Game_session_create_and_close_in_one_batch_call_the_existing_services_with_the_payload_fields()
        {
            _tx.Setup(t => t.CreateGameSession(null, 1, 2, 3, 1, "cashier@axis", 4, 5, It.IsAny<CancellationToken>(), 2, false, "offline start"))
                .ReturnsAsync(Ok(Tx(900, 7)));
            _tx.Setup(t => t.CloseGameSession(900, "cashier@axis", It.IsAny<CancellationToken>(), 2.5m))
                .ReturnsAsync(Ok(Tx(900, 6)));

            var createOp = Guid.NewGuid();
            var r = await Sync(
                Op(createOp, OfflineOpTypes.CreateGameSession, GameCreatePayload(gameId: 1, settingId: 2, hours: 3, setId: 4, persons: 2, discountId: 5)),
                Op(Guid.NewGuid(), OfflineOpTypes.CloseGameSession, new { invoiceId = -7, invoiceOpId = createOp, walletAmount = 2.5 }));

            r.Results.Should().HaveCount(2);
            r.Results.Select(x => x.Outcome).Should().AllBe(OfflineOpOutcomes.Ok);
            r.Results[0].Result!.Value.GetProperty("data").GetProperty("id").GetInt32().Should().Be(900);
            r.Results[1].Result!.Value.GetProperty("data").GetProperty("statusId").GetInt32().Should().Be(6);
            _tx.VerifyAll();

            _db.Set<IdempotencyRecord>().Select(x => x.Endpoint).Should()
                .BeEquivalentTo(new[] { OfflineOpTypes.CreateGameSession, OfflineOpTypes.CloseGameSession });
        }

        [Fact]
        public async Task Replaying_a_game_session_batch_returns_stored_responses_and_creates_nothing_new()
        {
            GameCreateReturns(901);
            _tx.Setup(t => t.CloseGameSession(901, "cashier@axis", It.IsAny<CancellationToken>(), 0m))
                .ReturnsAsync(Ok(Tx(901, 6)));
            var createOp = Guid.NewGuid();
            var ops = new[]
            {
                Op(createOp, OfflineOpTypes.CreateGameSession, GameCreatePayload()),
                Op(Guid.NewGuid(), OfflineOpTypes.CloseGameSession, new { invoiceId = -1, invoiceOpId = createOp }),
            };

            var first = await Sync(ops);
            var second = await Sync(ops);

            first.Results.Select(x => x.Outcome).Should().AllBe(OfflineOpOutcomes.Ok);
            second.Results.Select(x => x.Outcome).Should().AllBe(OfflineOpOutcomes.Replayed);
            second.Results[0].Result!.Value.GetRawText().Should().Be(first.Results[0].Result!.Value.GetRawText());
            second.Results[1].Result!.Value.GetRawText().Should().Be(first.Results[1].Result!.Value.GetRawText());

            _tx.Verify(t => t.CreateGameSession(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Once);
            _tx.Verify(t => t.CloseGameSession(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()), Times.Once);
            _db.Set<IdempotencyRecord>().Count().Should().Be(2);
        }

        [Fact]
        public async Task A_game_session_started_offline_can_be_closed_in_a_later_batch_through_its_create_opId()
        {
            GameCreateReturns(902);
            _tx.Setup(t => t.CloseGameSession(902, "cashier@axis", It.IsAny<CancellationToken>(), 0m))
                .ReturnsAsync(Ok(Tx(902, 6)));
            var createOp = Guid.NewGuid();

            var r1 = await Sync(Op(createOp, OfflineOpTypes.CreateGameSession, GameCreatePayload()));
            // a fresh service instance would resolve from the stored record the same way: the in-batch map is empty here
            var r2 = await Sync(Op(Guid.NewGuid(), OfflineOpTypes.CloseGameSession, new { invoiceId = -55, invoiceOpId = createOp }));

            r1.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Ok);
            r2.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Ok);
            _tx.Verify(t => t.CloseGameSession(902, "cashier@axis", It.IsAny<CancellationToken>(), 0m), Times.Once);
        }

        [Fact]
        public async Task Game_session_payload_validation_and_same_opId_conflict_follow_the_order_rules()
        {
            GameCreateReturns(903);
            var opId = Guid.NewGuid();

            var bad = await Sync(
                Op(Guid.NewGuid(), OfflineOpTypes.CreateGameSession, new { gameId = 0, gameSettingId = 1, hours = 1 }),
                Op(Guid.NewGuid(), OfflineOpTypes.CloseGameSession, new { invoiceId = -3, invoiceOpId = Guid.NewGuid() }),
                Op(opId, OfflineOpTypes.CreateGameSession, GameCreatePayload(hours: 1)));
            var conflict = await Sync(Op(opId, OfflineOpTypes.CreateGameSession, GameCreatePayload(hours: 2)));

            bad.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            bad.Results[0].Status.Should().Be(400);
            bad.Results[0].Error.Should().Contain("gameId");
            bad.Results[1].Outcome.Should().Be(OfflineOpOutcomes.Failed);
            bad.Results[1].Error.Should().Contain("not been created");
            bad.Results[2].Outcome.Should().Be(OfflineOpOutcomes.Ok);
            conflict.Results[0].Outcome.Should().Be(OfflineOpOutcomes.Conflict);
            conflict.Results[0].Status.Should().Be(409);

            _tx.Verify(t => t.CreateGameSession(
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Once);
            _db.Set<IdempotencyRecord>().Count().Should().Be(1);
        }

        [Fact]
        public void Request_hash_ignores_whitespace_but_not_values()
        {
            var a = OfflineSyncService.ComputeHash("X", JsonDocument.Parse("{\"a\":1,\"b\":[1,2]}").RootElement);
            var b = OfflineSyncService.ComputeHash("X", JsonDocument.Parse("{ \"a\" : 1 , \"b\" : [ 1 , 2 ] }").RootElement);
            var c = OfflineSyncService.ComputeHash("X", JsonDocument.Parse("{\"a\":2,\"b\":[1,2]}").RootElement);

            a.Should().Be(b);
            a.Should().NotBe(c);
            a.Should().HaveLength(64);
        }
    }
}
