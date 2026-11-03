using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Bi
{
    /// <summary>
    /// Branch-scoped sales queries shared by the BI report builders and the owner
    /// summary. The unscoped (branchId = null) paths keep using
    /// ITransactionRecordService; these helpers only exist because that service
    /// has no branch parameter. Attribution matches the consolidated dashboard:
    /// legacy transactions with BranchId = null belong to the default branch.
    /// </summary>
    internal static class BranchScopedSales
    {
        /// <summary>Scope describing one branch: its id and whether legacy (null) rows count towards it.</summary>
        internal sealed record Scope(int BranchId, bool IncludesLegacy, string Name);

        internal static async Task<Scope?> ResolveAsync(ApplicationDbContext db, int? branchId, CancellationToken ct)
        {
            if (!branchId.HasValue || branchId.Value <= 0) return null;
            var b = await db.Set<Branch>().AsNoTracking()
                .Where(x => x.Id == branchId.Value)
                .Select(x => new { x.Id, x.IsDefault, x.Name })
                .FirstOrDefaultAsync(ct);
            // Unknown id: still filter strictly on it so the report is honest (empty), never "all".
            return b is null ? new Scope(branchId.Value, false, $"#{branchId.Value}") : new Scope(b.Id, b.IsDefault, b.Name);
        }

        internal static IQueryable<TransactionRecord> Apply(IQueryable<TransactionRecord> q, Scope? scope)
        {
            if (scope is null) return q;
            return scope.IncludesLegacy
                ? q.Where(t => t.BranchId == scope.BranchId || t.BranchId == null)
                : q.Where(t => t.BranchId == scope.BranchId);
        }

        /// <summary>Paid transactions (status 6) whose business day falls in [from, toExclusive).</summary>
        internal static IQueryable<TransactionRecord> Paid(ApplicationDbContext db, DateTime from, DateTime toExclusive, Scope? scope) =>
            Apply(db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.StatusId == 6 && (t.PaidOn ?? t.CreatedOn) >= from && (t.PaidOn ?? t.CreatedOn) < toExclusive), scope);

        internal sealed record Totals(decimal Amount, int Orders)
        {
            public decimal AverageTicket => Orders == 0 ? 0 : Math.Round(Amount / Orders, 2);
        }

        internal static async Task<Totals> TotalsAsync(ApplicationDbContext db, DateTime from, DateTime toExclusive, Scope scope, CancellationToken ct)
        {
            var rows = await Paid(db, from, toExclusive, scope).Select(t => t.TotalPrice).ToListAsync(ct);
            return new Totals(Math.Round(rows.Sum(), 2), rows.Count);
        }

        internal sealed record DayTotals(DateTime Date, decimal Items, decimal Games)
        {
            public decimal Total => Items + Games;
        }

        /// <summary>Per business day: item (non-game) and game-session totals. Event tickets carry no branch and are excluded.</summary>
        internal static async Task<List<DayTotals>> DailyAsync(ApplicationDbContext db, DateTime from, DateTime toExclusive, Scope scope, CancellationToken ct)
        {
            var rows = await Paid(db, from, toExclusive, scope)
                .Select(t => new { Day = (t.PaidOn ?? t.CreatedOn).Date, t.TotalPrice, IsGame = t.GameId != null })
                .ToListAsync(ct);
            return rows.GroupBy(r => r.Day)
                .Select(g => new DayTotals(g.Key, g.Where(r => !r.IsGame).Sum(r => r.TotalPrice), g.Where(r => r.IsGame).Sum(r => r.TotalPrice)))
                .OrderBy(d => d.Date)
                .ToList();
        }

        internal sealed record ItemLine(int ItemId, string ItemName, string CategoryName, int Quantity, decimal Amount);

        /// <summary>Item lines of paid non-game transactions in the branch (amount = list price × quantity).</summary>
        internal static async Task<List<ItemLine>> ItemsAsync(ApplicationDbContext db, DateTime from, DateTime toExclusive, Scope scope, CancellationToken ct)
        {
            var lines = await Paid(db, from, toExclusive, scope)
                .Where(t => t.GameId == null)
                .SelectMany(t => t.TransactionItems.Where(ti => ti.Item != null).Select(ti => new
                {
                    ItemId = ti.Item.Id,
                    ItemName = ti.Item.Name,
                    CategoryName = ti.Item.Category != null ? ti.Item.Category.Name : "",
                    ti.Quantity,
                    Price = ti.Item.Price,
                }))
                .ToListAsync(ct);
            return lines.GroupBy(l => new { l.ItemId, l.ItemName, l.CategoryName })
                .Select(g => new ItemLine(g.Key.ItemId, g.Key.ItemName, g.Key.CategoryName, g.Sum(x => x.Quantity), g.Sum(x => x.Price * x.Quantity)))
                .OrderByDescending(l => l.Amount)
                .ToList();
        }
    }
}
