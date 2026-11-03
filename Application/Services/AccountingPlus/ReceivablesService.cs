using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.AccountingPlus
{
    public class ReceivablesService : IReceivablesService
    {
        public const int OpenInvoiceStatusId = 7;
        private readonly ApplicationDbContext _db;

        public ReceivablesService(ApplicationDbContext db) => _db = db;

        public async Task<ArAgingReportDto> GetArAgingAsync(DateTime? asOf, CancellationToken ct = default)
        {
            var at = AcctDates.Utc(asOf ?? DateTime.UtcNow).Date;
            var invoices = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.StatusId == OpenInvoiceStatusId && t.CreatedOn <= AcctDates.EndOfDay(at))
                .Select(t => new { t.Id, t.UserId, UserName = t.User != null ? t.User.UserName : null, t.CreatedOn, t.TotalPrice, t.WalletPaidAmount })
                .ToListAsync(ct);

            var rows = new List<ArAgingRowDto>();
            var totals = new AgingBuckets.Accumulator();
            foreach (var g in invoices.GroupBy(i => i.UserId))
            {
                var acc = new AgingBuckets.Accumulator();
                foreach (var i in g) acc.Add(i.CreatedOn, at, Math.Max(0, i.TotalPrice - i.WalletPaidAmount));
                totals.Add(acc);
                var name = g.Key is null ? "Walk-in" : (g.First().UserName ?? $"User #{g.Key}");
                rows.Add(new ArAgingRowDto(g.Key, name, acc.Count, acc.ToDto()));
            }
            return new ArAgingReportDto(at, rows.OrderByDescending(r => r.Buckets.Total).ToList(), totals.ToDto());
        }
    }
}
