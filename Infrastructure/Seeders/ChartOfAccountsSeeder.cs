using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Seeders
{
    public class ChartOfAccountsSeeder
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChartOfAccountsSeeder> _logger;

        public ChartOfAccountsSeeder(ApplicationDbContext context, ILogger<ChartOfAccountsSeeder> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            // Check if already seeded
            if (await _context.Accounts.AnyAsync())
            {
                _logger.LogInformation("Chart of Accounts already seeded");
                return;
            }

            _logger.LogInformation("Seeding Chart of Accounts...");

            // Get Account Types
            var assetType = await _context.AccountTypes.FirstAsync(at => at.TypeName == "Asset");
            var liabilityType = await _context.AccountTypes.FirstAsync(at => at.TypeName == "Liability");
            var equityType = await _context.AccountTypes.FirstAsync(at => at.TypeName == "Equity");
            var revenueType = await _context.AccountTypes.FirstAsync(at => at.TypeName == "Revenue");
            var expenseType = await _context.AccountTypes.FirstAsync(at => at.TypeName == "Expense");

            var accounts = new List<Account>
            {
                // ===== ASSETS (1000-1999) =====
                new Account
                {
                    AccountNumber = "1000",
                    AccountName = "Cash on Hand",
                    AccountTypeId = assetType.Id,
                    Description = "Petty cash and cash registers",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1100",
                    AccountName = "Bank Account - Primary",
                    AccountTypeId = assetType.Id,
                    Description = "Main business bank account",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1200",
                    AccountName = "Accounts Receivable",
                    AccountTypeId = assetType.Id,
                    Description = "Money owed by customers",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1300",
                    AccountName = "Inventory - FNB",
                    AccountTypeId = assetType.Id,
                    Description = "Food and beverage inventory",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1310",
                    AccountName = "Inventory - TCG Retail",
                    AccountTypeId = assetType.Id,
                    Description = "Trading card game inventory",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1400",
                    AccountName = "Prepaid Expenses",
                    AccountTypeId = assetType.Id,
                    Description = "Expenses paid in advance",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1500",
                    AccountName = "Gaming Equipment",
                    AccountTypeId = assetType.Id,
                    Description = "PS5 consoles, VR headsets, controllers",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1510",
                    AccountName = "Furniture & Fixtures",
                    AccountTypeId = assetType.Id,
                    Description = "Chairs, tables, sofas, shelving",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1520",
                    AccountName = "Computer Equipment",
                    AccountTypeId = assetType.Id,
                    Description = "POS systems, servers, computers",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "1600",
                    AccountName = "Accumulated Depreciation",
                    AccountTypeId = assetType.Id,
                    Description = "Contra-asset account for depreciation",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },

                // ===== LIABILITIES (2000-2999) =====
                new Account
                {
                    AccountNumber = "2000",
                    AccountName = "Accounts Payable",
                    AccountTypeId = liabilityType.Id,
                    Description = "Money owed to suppliers",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "2100",
                    AccountName = "VAT Payable",
                    AccountTypeId = liabilityType.Id,
                    Description = "Value Added Tax owed to government",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "2200",
                    AccountName = "Salaries Payable",
                    AccountTypeId = liabilityType.Id,
                    Description = "Unpaid employee salaries",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "2300",
                    AccountName = "Loans Payable",
                    AccountTypeId = liabilityType.Id,
                    Description = "Bank loans and financing",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "2400",
                    AccountName = "Customer Deposits",
                    AccountTypeId = liabilityType.Id,
                    Description = "Prepaid customer deposits",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },

                // ===== EQUITY (3000-3999) =====
                new Account
                {
                    AccountNumber = "3000",
                    AccountName = "Owner's Capital",
                    AccountTypeId = equityType.Id,
                    Description = "Initial and additional capital invested",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "3100",
                    AccountName = "Retained Earnings",
                    AccountTypeId = equityType.Id,
                    Description = "Accumulated profits from prior periods",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "3200",
                    AccountName = "Current Year Profit/Loss",
                    AccountTypeId = equityType.Id,
                    Description = "Net income for current fiscal year",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },

                // ===== REVENUE (4000-4999) =====
                new Account
                {
                    AccountNumber = "4000",
                    AccountName = "Gaming Revenue - PS5",
                    AccountTypeId = revenueType.Id,
                    Description = "Revenue from PS5 gaming sessions",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "4010",
                    AccountName = "Gaming Revenue - VR",
                    AccountTypeId = revenueType.Id,
                    Description = "Revenue from VR gaming sessions",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "4020",
                    AccountName = "Gaming Revenue - Board Games",
                    AccountTypeId = revenueType.Id,
                    Description = "Revenue from board game sessions",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "4100",
                    AccountName = "FNB Revenue - Food",
                    AccountTypeId = revenueType.Id,
                    Description = "Revenue from food sales",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "4110",
                    AccountName = "FNB Revenue - Beverages",
                    AccountTypeId = revenueType.Id,
                    Description = "Revenue from beverage sales",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "4200",
                    AccountName = "TCG Retail Revenue",
                    AccountTypeId = revenueType.Id,
                    Description = "Revenue from trading card game sales",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },

                // ===== EXPENSES (5000-5999) =====
                new Account
                {
                    AccountNumber = "5000",
                    AccountName = "COGS - FNB",
                    AccountTypeId = expenseType.Id,
                    Description = "Cost of goods sold for food & beverages",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5010",
                    AccountName = "COGS - TCG Retail",
                    AccountTypeId = expenseType.Id,
                    Description = "Cost of goods sold for trading cards",
                    IsSystemAccount = true,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5100",
                    AccountName = "Rent Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Monthly rent payments",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5200",
                    AccountName = "Utilities Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Electricity, water, gas",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5300",
                    AccountName = "Internet & Telecom Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Internet, phone, communication",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5400",
                    AccountName = "Salaries & Wages Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Employee compensation",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5500",
                    AccountName = "Marketing & Advertising Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Promotional activities",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5600",
                    AccountName = "Maintenance & Repairs Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Equipment and facility maintenance",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5700",
                    AccountName = "Depreciation Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Non-cash depreciation of assets",
                    IsSystemAccount = true,
                    AllowManualEntry = false
                },
                new Account
                {
                    AccountNumber = "5800",
                    AccountName = "Office Supplies Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Stationery, consumables",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                },
                new Account
                {
                    AccountNumber = "5900",
                    AccountName = "Miscellaneous Expense",
                    AccountTypeId = expenseType.Id,
                    Description = "Other operating expenses",
                    IsSystemAccount = false,
                    AllowManualEntry = true
                }
            };

            await _context.Accounts.AddRangeAsync(accounts);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Successfully seeded {accounts.Count} accounts");
        }

        public static async Task SeedChartOfAccountsAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ChartOfAccountsSeeder>>();

            var seeder = new ChartOfAccountsSeeder(context, logger);
            await seeder.SeedAsync();
        }
    }
}