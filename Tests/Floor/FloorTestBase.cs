using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Floor;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests.Floor
{
    internal sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId => 1;
        public string UserName => "cashier@test";
        public IReadOnlyList<string> Roles => new[] { "cashier" };
        public int? BranchId => null;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    /// <summary>Records every WhatsApp send; <see cref="FailNext"/> makes the next send fail.</summary>
    public sealed class FakeWhatsApp : IWhatsAppService
    {
        public readonly List<(string Phone, string Template, List<string> Params)> Sent = new();
        public bool FailNext;
        public Task<WhatsAppBlastResult> SendBlastAsync(int? pendingActionId, IEnumerable<WhatsAppRecipient> recipients, string templateName,
            IEnumerable<string> templateParams, CancellationToken ct = default)
        {
            if (FailNext)
            {
                FailNext = false;
                return Task.FromResult(new WhatsAppBlastResult(0, 1, new[] { "HTTP 500" }));
            }
            var list = recipients.ToList();
            foreach (var r in list) Sent.Add((r.Phone, templateName, (r.PerRecipientParams ?? templateParams).ToList()));
            return Task.FromResult(new WhatsAppBlastResult(list.Count, 0, Array.Empty<string>()));
        }
    }

    public sealed class FakeSettings : IIntegrationSettingsService
    {
        public readonly Dictionary<string, string?> Values = new();
        public Task<IReadOnlyList<IntegrationSettingDto>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntegrationSettingDto>>(Array.Empty<IntegrationSettingDto>());
        public Task<string?> GetRawAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
        public Task UpsertAsync(string key, string? value, string? actor, CancellationToken ct = default) { Values[key] = value; return Task.CompletedTask; }
        public Task<IntegrationTestResultDto> TestAnthropicAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IntegrationTestResultDto> TestWhatsAppAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Shared in-memory context + service wiring for the Floor tests.</summary>
    public abstract class FloorTestBase : IDisposable
    {
        protected readonly ApplicationDbContext Db;
        protected readonly Mock<ITransactionRecordService> Trx = new();
        protected readonly TableSessionService Sessions;
        protected readonly FloorBillingService Billing;
        protected readonly ReservationService Reservations;
        protected readonly FloorService Floor;
        protected readonly FakeWhatsApp WhatsApp = new();
        protected readonly FakeSettings Settings = new();
        protected readonly ReservationNotificationService Notifications;

        protected FloorTestBase()
        {
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var area = new FloorArea { Id = 1, Name = "Main", SortOrder = 1, IsActive = true };
            Db.Add(area);
            Db.Add(new DiningTable { Id = 1, AreaId = 1, Code = "T1", Name = "Table 1", Capacity = 4 });
            Db.Add(new DiningTable { Id = 2, AreaId = 1, Code = "T2", Name = "Table 2", Capacity = 2 });
            Db.Add(new DiningTable { Id = 3, AreaId = 1, Code = "T3", Name = "Table 3", Capacity = 6, IsActive = false });
            Db.Add(new Item { Id = 1, Name = "Burger", Price = 10m, Type = "CoffeeShop", CategoryId = 1, StatusId = 1, Quantity = 100 });
            Db.Add(new Item { Id = 2, Name = "Cola", Price = 2.5m, Type = "CoffeeShop", CategoryId = 1, StatusId = 1, Quantity = 100 });
            Db.SaveChanges();

            var user = new FakeUser();
            Sessions = new TableSessionService(Db, Trx.Object, user, NullLogger<TableSessionService>.Instance);
            Billing = new FloorBillingService(Db, user, NullLogger<FloorBillingService>.Instance);
            Notifications = new ReservationNotificationService(Db, WhatsApp, Settings, NullLogger<ReservationNotificationService>.Instance);
            Reservations = new ReservationService(Db, Sessions, user, NullLogger<ReservationService>.Instance, Notifications);
            Floor = new FloorService(Db, user, NullLogger<FloorService>.Instance);
        }

        /// <summary>Seeds an invoice with the given (itemId, qty) lines; total = Σ price × qty.</summary>
        protected TransactionRecord SeedInvoice(int id, int statusId = 7, params (int itemId, int qty)[] lines)
        {
            var prices = new Dictionary<int, decimal> { { 1, 10m }, { 2, 2.5m } };
            var trx = new TransactionRecord
            {
                Id = id, StatusId = statusId, CreatedBy = "seed", CreatedOn = DateTime.UtcNow, FK_FoodStatusId = 11,
                TotalPrice = lines.Sum(l => prices[l.itemId] * l.qty),
            };
            Db.Add(trx);
            foreach (var (itemId, qty) in lines)
                Db.Add(new TransactionItem { TransactionRecordId = id, ItemId = itemId, Quantity = qty });
            Db.SaveChanges();
            return trx;
        }

        public void Dispose() => Db.Dispose();
    }
}
