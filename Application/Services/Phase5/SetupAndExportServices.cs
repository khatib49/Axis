using System.Globalization;
using System.Text;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Phase5
{
    public interface ISetupService
    {
        Task<SetupStatusDto> StatusAsync(CancellationToken ct = default);
        Task<SetupStatusDto> SaveBusinessAsync(SetupBusinessDto dto, string actor, CancellationToken ct = default);
    }

    /// <summary>"Setup in minutes": the checklist a new venue walks through, computed from real data.</summary>
    public class SetupService : ISetupService
    {
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        public SetupService(ApplicationDbContext db, IIntegrationSettingsService settings) { _db = db; _settings = settings; }

        private async Task<bool> HasAsync(string key, CancellationToken ct) => !string.IsNullOrWhiteSpace(await _settings.GetRawAsync(key, ct));

        public async Task<SetupStatusDto> StatusAsync(CancellationToken ct = default)
        {
            var name = await _settings.GetRawAsync("Business.Name", ct);
            var items = await _db.Set<Item>().CountAsync(i => i.StatusId == 1, ct);
            var rates = await _db.Set<ExchangeRate>().CountAsync(ct);
            var sets = await _db.Set<Set>().CountAsync(ct);
            var tables = await _db.Set<DiningTable>().CountAsync(ct);
            var staff = await (from ur in _db.UserRoles join r in _db.Roles on ur.RoleId equals r.Id where r.Name == "cashier" || r.Name == "gamecashier" || r.Name == "chef" select ur.UserId).Distinct().CountAsync(ct);
            var printers = await _db.Set<Printer>().CountAsync(ct);
            var payments = await HasAsync("Whish.Channel", ct) || await _db.Set<CardTerminal>().AnyAsync(t => t.IsActive, ct);
            var owner = await HasAsync(OwnerNotifier.TelegramTokenKey, ct) || await HasAsync("WhatsApp.AccessToken", ct);
            var ai = await HasAsync("Anthropic.ApiKey", ct);
            var steps = new List<SetupStepDto>
            {
                new("business", "Business details", !string.IsNullOrWhiteSpace(name), name ?? "Name, phone, language and time zone", "/admin/setup"),
                new("menu", "Menu", items > 0, items > 0 ? $"{items} items" : "Import from a photo, PDF or CSV", "/admin/ai/menu-import"),
                new("currency", "Exchange rate (USD / LBP)", rates > 0, rates > 0 ? $"{rates} rate(s)" : "Set today's rate", "/admin/currencies"),
                new("places", "Rooms, stations and tables", sets + tables > 0, $"{sets} stations, {tables} tables", "/admin/rooms"),
                new("staff", "Staff accounts", staff > 0, staff > 0 ? $"{staff} staff" : "Add cashiers and kitchen", "/admin/users"),
                new("printers", "Printers", printers > 0, printers > 0 ? $"{printers} printer(s)" : "Receipt and kitchen printers (set up by your installer)", ""),
                new("payments", "Payments (Whish / card terminal)", payments, payments ? "Ready" : "Connect Whish or add a card terminal", "/admin/payments/terminals"),
                new("owner", "Owner phone (Telegram / WhatsApp)", owner, owner ? "Connected" : "Get alerts and ask questions from your phone", "/admin/owner-assistant"),
                new("ai", "AI assistant key", ai, ai ? "Ready" : "Unlocks menu photos, invoice scan and free questions", "/admin/integrations"),
            };
            return new(steps.Count(s => s.Done), steps.Count, steps, name, await _settings.GetRawAsync("Business.Language", ct) ?? "en");
        }

        public async Task<SetupStatusDto> SaveBusinessAsync(SetupBusinessDto dto, string actor, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Business name is required.");
            await _settings.UpsertAsync("Business.Name", dto.Name.Trim(), actor, ct);
            if (dto.Phone is not null) await _settings.UpsertAsync("Business.Phone", dto.Phone.Trim(), actor, ct);
            if (dto.Address is not null) await _settings.UpsertAsync("Business.Address", dto.Address.Trim(), actor, ct);
            if (dto.Language is "en" or "ar") await _settings.UpsertAsync("Business.Language", dto.Language, actor, ct);
            if (!string.IsNullOrWhiteSpace(dto.TimeZone))
            {
                try { TimeZoneInfo.FindSystemTimeZoneById(dto.TimeZone.Trim()); } catch (Exception) { throw new ArgumentException("Unknown time zone (use e.g. Asia/Beirut)."); }
                await _settings.UpsertAsync("Venue.TimeZone", dto.TimeZone.Trim(), actor, ct);
            }
            return await StatusAsync(ct);
        }
    }

    public interface IAccountingExportService
    {
        Task<(string FileName, string Csv, int Entries)> ExportAsync(string format, DateTime from, DateTime to, CancellationToken ct = default);
        Task<(string FileName, string Csv)> ChartOfAccountsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Posted, non-voided journal entries as CSV for Xero (manual journal import),
    /// QuickBooks Online (journal entry import), Odoo (account.move import) or a plain file.
    /// </summary>
    public class AccountingExportService : IAccountingExportService
    {
        public static readonly string[] Formats = { "xero", "quickbooks", "odoo", "csv" };
        private readonly ApplicationDbContext _db;
        public AccountingExportService(ApplicationDbContext db) { _db = db; }

        private record Row(string Number, DateTime Date, string Description, string Ref, int Line, string AccNo, string AccName, decimal Debit, decimal Credit, string? LineDesc);

        public async Task<(string FileName, string Csv, int Entries)> ExportAsync(string format, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            format = (format ?? "").ToLowerInvariant();
            if (!Formats.Contains(format)) throw new ArgumentException($"Format must be one of {string.Join(", ", Formats)}.");
            if (toUtc <= fromUtc) throw new ArgumentException("'to' must be after 'from'.");
            if ((toUtc - fromUtc).TotalDays > 366) throw new ArgumentException("Export at most one year at a time.");
            var rows = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                              join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                              join a in _db.Set<Account>().AsNoTracking() on l.AccountId equals a.Id
                              where e.IsPosted && !e.IsVoided && e.EntryDate >= fromUtc && e.EntryDate < toUtc
                              orderby e.EntryDate, e.Id, l.LineNumber
                              select new Row(e.EntryNumber, e.EntryDate, e.Description, e.ReferenceType + (e.ReferenceId != null ? " #" + e.ReferenceId : ""), l.LineNumber, a.AccountNumber, a.AccountName, l.DebitAmount, l.CreditAmount, l.Description))
                .ToListAsync(ct);
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            string M(decimal v) => v.ToString("0.00", inv);
            switch (format)
            {
                case "xero":
                    sb.AppendLine("*Narration,*Date,Description,*AccountCode,*TaxRate,*Amount,TrackingName1,TrackingOption1");
                    foreach (var r in rows)
                        sb.AppendLine(Csv($"{r.Number} {r.Description}", r.Date.ToString("dd/MM/yyyy", inv), r.LineDesc ?? r.Description, r.AccNo, "Tax Exempt", M(r.Debit - r.Credit), "", ""));
                    break;
                case "quickbooks":
                    sb.AppendLine("Journal No,Journal Date,Account,Debits,Credits,Description,Name,Currency");
                    foreach (var r in rows)
                        sb.AppendLine(Csv(r.Number, r.Date.ToString("MM/dd/yyyy", inv), $"{r.AccNo} {r.AccName}", r.Debit > 0 ? M(r.Debit) : "", r.Credit > 0 ? M(r.Credit) : "", r.LineDesc ?? r.Description, "", "USD"));
                    break;
                case "odoo":
                    sb.AppendLine("Reference,Date,Journal,Line Items/Account,Line Items/Label,Line Items/Debit,Line Items/Credit");
                    string? last = null;
                    foreach (var r in rows)
                    {
                        var first = r.Number != last; last = r.Number;
                        sb.AppendLine(Csv(first ? r.Number : "", first ? r.Date.ToString("yyyy-MM-dd", inv) : "", first ? "Miscellaneous Operations" : "", r.AccNo, r.LineDesc ?? r.Description, M(r.Debit), M(r.Credit)));
                    }
                    break;
                default:
                    sb.AppendLine("Entry,Date,Description,Reference,Line,Account,Account name,Debit,Credit,Line description");
                    foreach (var r in rows)
                        sb.AppendLine(Csv(r.Number, r.Date.ToString("yyyy-MM-dd", inv), r.Description, r.Ref, r.Line.ToString(inv), r.AccNo, r.AccName, M(r.Debit), M(r.Credit), r.LineDesc ?? ""));
                    break;
            }
            return ($"axis-journals-{format}-{fromUtc:yyyyMMdd}-{toUtc.AddDays(-1):yyyyMMdd}.csv", sb.ToString(), rows.Select(r => r.Number).Distinct().Count());
        }

        public async Task<(string FileName, string Csv)> ChartOfAccountsAsync(CancellationToken ct = default)
        {
            var accounts = await (from a in _db.Set<Account>().AsNoTracking()
                                  join t in _db.Set<AccountType>().AsNoTracking() on a.AccountTypeId equals t.Id
                                  orderby a.AccountNumber select new { a.AccountNumber, a.AccountName, Type = t.TypeName, a.IsActive, a.CurrentBalance }).ToListAsync(ct);
            var sb = new StringBuilder("*Code,*Name,*Type,Status,Balance\n");
            foreach (var a in accounts) sb.AppendLine(Csv(a.AccountNumber, a.AccountName, a.Type, a.IsActive ? "Active" : "Archived", a.CurrentBalance.ToString("0.00", CultureInfo.InvariantCulture)));
            return ("axis-chart-of-accounts.csv", sb.ToString());
        }

        /// <summary>RFC 4180 quoting; also neutralises spreadsheet formula injection.</summary>
        public static string Csv(params string[] cells) => string.Join(",", cells.Select(c =>
        {
            var v = c ?? "";
            if (v.Length > 0 && "=+-@".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) v = "'" + v;
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }));
    }
}
