using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class ProfitService : IProfitService
    {
        private readonly IBaseRepository<TransactionRecord> _transactionRepo;
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<ExpenseCategory> _expenseCategoryRepo;
        private readonly IBaseRepository<Category> _categoryRepo;

        public ProfitService(
            IBaseRepository<TransactionRecord> transactionRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<ExpenseCategory> expenseCategoryRepo,
            IBaseRepository<Category> categoryRepo)
        {
            _transactionRepo = transactionRepo;
            _expenseRepo = expenseRepo;
            _expenseCategoryRepo = expenseCategoryRepo;
            _categoryRepo = categoryRepo;
        }

        public async Task<ProfitDto> CalculateFnbProfitAsync(
            DateTime? from,
            DateTime? to,
            string? categoryIds,
            CancellationToken ct = default)
        {
            var catList = ParseCategoryIds(categoryIds);
            var tcgCategoryIds = await GetTcgRetailCategoryIds(ct);

            // FNB Revenue: item transactions excluding TCG Retail
            var revenueQuery = _transactionRepo.Query()
                .Where(t => t.StatusId == 6)
                .Where(t => t.GameId == null);

            if (from.HasValue)
                revenueQuery = revenueQuery.Where(t => t.CreatedOn >= from.Value);
            if (to.HasValue)
                revenueQuery = revenueQuery.Where(t => t.CreatedOn < to.Value.AddDays(1));

            // Exclude TCG Retail items
            revenueQuery = revenueQuery.Where(t =>
                !t.TransactionItems.Any(ti => ti.Item != null && tcgCategoryIds.Contains(ti.Item.CategoryId)));

            if (catList.Count > 0)
            {
                revenueQuery = revenueQuery.Where(t =>
                    t.TransactionItems.Any(ti => ti.Item != null && catList.Contains(ti.Item.CategoryId)));
            }

            var totalRevenue = await revenueQuery
                .SelectMany(t => t.TransactionItems
                    .Where(ti => ti.Item != null && !tcgCategoryIds.Contains(ti.Item.CategoryId))
                    .Select(ti => (ti.Item!.Price * ti.Quantity)))
                .SumAsync(x => (decimal?)x, ct) ?? 0m;

            var transactionCount = await revenueQuery.CountAsync(ct);

            // FNB Expenses
            var totalExpenses = await CalculateExpensesAsync(from, to, await GetFnbExpenseCategoryIds(ct), catList, ct);

            var netProfit = totalRevenue - totalExpenses;
            var profitMargin = totalRevenue > 0 ? (netProfit / totalRevenue) * 100 : 0;

            return new ProfitDto(
                TotalRevenue: totalRevenue,
                TotalExpenses: totalExpenses,
                NetProfit: netProfit,
                ProfitMargin: Math.Round(profitMargin, 2),
                TransactionCount: transactionCount,
                FromDate: from,
                ToDate: to
            );
        }

        public async Task<ProfitDto> CalculateGamingProfitAsync(
            DateTime? from,
            DateTime? to,
            string? categoryIds,
            CancellationToken ct = default)
        {
            var catList = ParseCategoryIds(categoryIds);

            // Gaming Revenue: game sessions only
            var revenueQuery = _transactionRepo.Query()
                .Where(t => t.StatusId == 6)
                .Where(t => t.GameId != null);

            if (from.HasValue)
                revenueQuery = revenueQuery.Where(t => t.CreatedOn >= from.Value);
            if (to.HasValue)
                revenueQuery = revenueQuery.Where(t => t.CreatedOn < to.Value.AddDays(1));

            if (catList.Count > 0)
            {
                revenueQuery = revenueQuery.Where(t =>
                    t.Game != null && catList.Contains(t.Game.CategoryId));
            }

            var totalRevenue = await revenueQuery
                .SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0m;

            var transactionCount = await revenueQuery.CountAsync(ct);

            // Gaming Expenses
            var totalExpenses = await CalculateExpensesAsync(from, to, await GetGamingExpenseCategoryIds(ct), catList, ct);

            var netProfit = totalRevenue - totalExpenses;
            var profitMargin = totalRevenue > 0 ? (netProfit / totalRevenue) * 100 : 0;

            return new ProfitDto(
                TotalRevenue: totalRevenue,
                TotalExpenses: totalExpenses,
                NetProfit: netProfit,
                ProfitMargin: Math.Round(profitMargin, 2),
                TransactionCount: transactionCount,
                FromDate: from,
                ToDate: to
            );
        }

        public async Task<ProfitDto> CalculateTcgRetailProfitAsync(
            DateTime? from,
            DateTime? to,
            string? categoryIds,
            CancellationToken ct = default)
        {
            var catList = ParseCategoryIds(categoryIds);
            var tcgCategoryIds = await GetTcgRetailCategoryIds(ct);

            if (tcgCategoryIds.Count == 0)
            {
                return new ProfitDto(0, 0, 0, 0, 0, from, to);
            }

            // TCG Retail Revenue: item transactions with TCG Retail categories only
            var revenueQuery = _transactionRepo.Query()
                .Where(t => t.StatusId == 6)
                .Where(t => t.GameId == null)
                .Where(t => t.TransactionItems.Any(ti =>
                    ti.Item != null && tcgCategoryIds.Contains(ti.Item.CategoryId)));

            if (from.HasValue)
                revenueQuery = revenueQuery.Where(t => t.CreatedOn >= from.Value);
            if (to.HasValue)
                revenueQuery = revenueQuery.Where(t => t.CreatedOn < to.Value.AddDays(1));

            if (catList.Count > 0)
            {
                revenueQuery = revenueQuery.Where(t =>
                    t.TransactionItems.Any(ti => ti.Item != null &&
                        catList.Contains(ti.Item.CategoryId) &&
                        tcgCategoryIds.Contains(ti.Item.CategoryId)));
            }

            var totalRevenue = await revenueQuery
                .SelectMany(t => t.TransactionItems
                    .Where(ti => ti.Item != null && tcgCategoryIds.Contains(ti.Item.CategoryId))
                    .Select(ti => (ti.Item!.Price * ti.Quantity)))
                .SumAsync(x => (decimal?)x, ct) ?? 0m;

            var transactionCount = await revenueQuery.CountAsync(ct);

            // TCG Retail Expenses
            var totalExpenses = await CalculateExpensesAsync(from, to, await GetTcgRetailExpenseCategoryIds(ct), catList, ct);

            var netProfit = totalRevenue - totalExpenses;
            var profitMargin = totalRevenue > 0 ? (netProfit / totalRevenue) * 100 : 0;

            return new ProfitDto(
                TotalRevenue: totalRevenue,
                TotalExpenses: totalExpenses,
                NetProfit: netProfit,
                ProfitMargin: Math.Round(profitMargin, 2),
                TransactionCount: transactionCount,
                FromDate: from,
                ToDate: to
            );
        }

        public async Task<DetailedOverallProfitDto> CalculateOverallProfitAsync(
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default)
        {
            // Calculate all three segments
            var fnbProfit = await CalculateFnbProfitAsync(from, to, null, ct);
            var gamingProfit = await CalculateGamingProfitAsync(from, to, null, ct);
            var tcgRetailProfit = await CalculateTcgRetailProfitAsync(from, to, null, ct);

            // Combine totals
            var totalRevenue = fnbProfit.TotalRevenue + gamingProfit.TotalRevenue + tcgRetailProfit.TotalRevenue;
            var totalExpenses = fnbProfit.TotalExpenses + gamingProfit.TotalExpenses + tcgRetailProfit.TotalExpenses;
            var netProfit = totalRevenue - totalExpenses;
            var overallMargin = totalRevenue > 0 ? (netProfit / totalRevenue) * 100 : 0;

            return new DetailedOverallProfitDto(
                FnbProfit: fnbProfit,
                GamingProfit: gamingProfit,
                TcgRetailProfit: tcgRetailProfit,
                TotalRevenue: totalRevenue,
                TotalExpenses: totalExpenses,
                NetProfit: netProfit,
                OverallProfitMargin: Math.Round(overallMargin, 2),
                FromDate: from,
                ToDate: to
            );
        }

        // Helper method for calculating expenses
        private async Task<decimal> CalculateExpensesAsync(
            DateTime? from,
            DateTime? to,
            List<int> expenseCategoryIds,
            List<int> filterCategoryIds,
            CancellationToken ct)
        {
            var expenseQuery = _expenseRepo.Query();

            if (from.HasValue)
                expenseQuery = expenseQuery.Where(e => e.ToDate >= from.Value);
            if (to.HasValue)
                expenseQuery = expenseQuery.Where(e => e.FromDate <= to.Value);

            if (expenseCategoryIds.Count > 0)
            {
                expenseQuery = expenseQuery.Where(e => expenseCategoryIds.Contains(e.FK_CategoryId));
            }

            if (filterCategoryIds.Count > 0)
            {
                expenseQuery = expenseQuery.Where(e => filterCategoryIds.Contains(e.FK_CategoryId));
            }

            return await expenseQuery.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        }

        // Helper methods to identify category types
        private async Task<List<int>> GetTcgRetailCategoryIds(CancellationToken ct)
        {
            return await _categoryRepo.Query()
                .Where(c => c.Name.ToLower().Contains("tcg retail") ||
                           c.Name.ToLower() == "tcg" ||
                           c.Type.ToLower().Contains("tcg"))
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        private async Task<List<int>> GetFnbExpenseCategoryIds(CancellationToken ct)
        {
            return await _expenseCategoryRepo.Query()
                .Where(c => c.Name.ToLower().Contains("food") ||
                           c.Name.ToLower().Contains("beverage") ||
                           c.Name.ToLower().Contains("kitchen") ||
                           c.Name.ToLower().Contains("coffee") ||
                           c.Name.ToLower().Contains("fnb"))
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        private async Task<List<int>> GetGamingExpenseCategoryIds(CancellationToken ct)
        {
            return await _expenseCategoryRepo.Query()
                .Where(c => (c.Name.ToLower().Contains("game") ||
                            c.Name.ToLower().Contains("gaming") ||
                            c.Name.ToLower().Contains("console") ||
                            c.Name.ToLower().Contains("ps5") ||
                            c.Name.ToLower().Contains("vr") ||
                            c.Name.ToLower().Contains("board")) &&
                            !c.Name.ToLower().Contains("tcg"))
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        private async Task<List<int>> GetTcgRetailExpenseCategoryIds(CancellationToken ct)
        {
            return await _expenseCategoryRepo.Query()
                .Where(c => c.Name.ToLower().Contains("tcg") ||
                           c.Name.ToLower().Contains("card") ||
                           c.Name.ToLower().Contains("trading"))
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        private static List<int> ParseCategoryIds(string? categoryIds)
        {
            if (string.IsNullOrWhiteSpace(categoryIds))
                return new List<int>();

            return categoryIds
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();
        }
    }
}
