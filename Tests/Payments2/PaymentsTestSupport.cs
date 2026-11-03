using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Payments2;
using Application.Services.TillControls;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests.Payments2
{
    public sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId { get; set; } = 1;
        public string UserName { get; set; } = "cashier@axislb.com";
        public IReadOnlyList<string> Roles { get; set; } = new[] { "cashier" };
        public int? BranchId { get; set; }
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// In-memory db + mocked Whish client / tender service / transaction service.
    /// The tender mock keeps a running "paid" amount so remaining math is realistic.
    /// </summary>
    public sealed class PaymentsFixture : IDisposable
    {
        public const string Success = "https://api.test/return?requestId={requestId}&result=success";
        public const string Cancel = "https://api.test/return?requestId={requestId}&result=cancel";

        public ApplicationDbContext Db { get; }
        public FakeUser User { get; } = new();
        public Mock<IWhishCollectClient> Whish { get; } = new();
        public Mock<ITenderService> Tenders { get; } = new();
        public Mock<ITransactionRecordService> Transactions { get; } = new();
        public CurrencyService Currency { get; }
        public WhishInvoicePaymentService Payments { get; }
        public DisplayStateService Displays { get; }
        public List<AddTendersDto> TenderCalls { get; } = new();
        public Dictionary<int, decimal> Paid { get; } = new();

        public PaymentsFixture()
        {
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            Db.Add(new Status { Id = 6, Name = "Paid" });
            Db.Add(new Status { Id = 7, Name = "Open" });
            Db.Add(new AppUser { Id = 1, UserName = "cashier@axislb.com", StatusId = 6 });
            Db.SaveChanges();

            Whish.Setup(w => w.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Whish.Setup(w => w.CreateCollectAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string ext, decimal amt, string cur, string desc, string ok, string ko, CancellationToken _) =>
                    new WhishCollectResult(true, $"https://whish.test/collect/{ext}", null, "{\"status\":true}"));
            Whish.Setup(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WhishCollectStatus("pending", null));

            Tenders.Setup(t => t.SummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int tx, CancellationToken _) => Summary(tx));
            Tenders.Setup(t => t.AddAsync(It.IsAny<AddTendersDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AddTendersDto dto, CancellationToken _) =>
                {
                    TenderCalls.Add(dto);
                    Paid[dto.TransactionId] = Paid.GetValueOrDefault(dto.TransactionId) + dto.Tenders.Sum(l => l.Amount);
                    return Summary(dto.TransactionId);
                });

            Transactions.Setup(t => t.CloseOpenInvoice(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()))
                .ReturnsAsync((int id, string by, CancellationToken _, decimal w) =>
                {
                    var tx = Db.Set<TransactionRecord>().First(t => t.Id == id);
                    tx.StatusId = 6; tx.PaidOn = DateTime.UtcNow;
                    Db.SaveChanges();
                    return new BaseResponse<TransactionDto>(true, null, "closed");
                });

            Currency = new CurrencyService(Db, User, NullLogger<CurrencyService>.Instance);
            Payments = new WhishInvoicePaymentService(Db, Whish.Object, Tenders.Object, Transactions.Object, User, NullLogger<WhishInvoicePaymentService>.Instance);
            Displays = new DisplayStateService(Db, Currency, Payments, User, NullLogger<DisplayStateService>.Instance);
        }

        private BaseResponse<TenderSummaryDto> Summary(int txId)
        {
            var tx = Db.Set<TransactionRecord>().AsNoTracking().FirstOrDefault(t => t.Id == txId);
            if (tx is null) return new(false, "NotFound", "Transaction not found.");
            var paid = Paid.GetValueOrDefault(txId);
            var tenders = TenderCalls.Where(c => c.TransactionId == txId).SelectMany(c => c.Tenders)
                .Select((l, i) => new PaymentTenderDto(i + 1, txId, null, l.CurrencyCode, l.Amount, 1, l.Amount, l.Method, l.Reference, DateTime.UtcNow, "test")).ToList();
            return new(true, null, "OK", new TenderSummaryDto(txId, "USD", tx.TotalPrice, paid, Math.Max(0, tx.TotalPrice - paid), Math.Max(0, paid - tx.TotalPrice), new(), tenders));
        }

        public TransactionRecord AddTransaction(int id, decimal total, int statusId = 7)
        {
            var t = new TransactionRecord { Id = id, StatusId = statusId, TotalPrice = total, CreatedBy = "cashier@axislb.com", CreatedOn = DateTime.UtcNow };
            Db.Add(t);
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return t;
        }

        public void Dispose() => Db.Dispose();
    }
}
