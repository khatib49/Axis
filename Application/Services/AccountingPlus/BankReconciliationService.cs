using System.Globalization;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.AccountingPlus
{
    public class BankReconciliationService : IBankReconciliationService
    {
        private const int SuggestionWindowDays = 3;

        private readonly ApplicationDbContext _db;
        private readonly IAccountingPeriodService _periods;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<BankReconciliationService> _logger;

        public BankReconciliationService(
            ApplicationDbContext db,
            IAccountingPeriodService periods,
            ICurrentUserAccessor user,
            ILogger<BankReconciliationService> logger)
        {
            _db = db;
            _periods = periods;
            _user = user;
            _logger = logger;
        }

        // ============================================
        // BANK ACCOUNTS
        // ============================================

        public async Task<List<BankAccountDto>> ListBankAccountsAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<BankAccount>().AsNoTracking();
            if (!includeInactive) q = q.Where(b => b.IsActive);
            var rows = await q.OrderBy(b => b.Name).ToListAsync(ct);
            var accts = await LoadAccountsAsync(rows.Select(r => r.GlAccountId), ct);
            return rows.Select(r => ToDto(r, accts)).ToList();
        }

        public async Task<BaseResponse<BankAccountDto>> GetBankAccountAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.Set<BankAccount>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return new BaseResponse<BankAccountDto>(false, "Bank account not found", "");
            var accts = await LoadAccountsAsync(new[] { b.GlAccountId }, ct);
            return new BaseResponse<BankAccountDto>(true, null, "", ToDto(b, accts));
        }

        public async Task<BaseResponse<BankAccountDto>> CreateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateBankAccountAsync(dto, ct);
            if (err is not null) return new BaseResponse<BankAccountDto>(false, err, "");

            var b = new BankAccount
            {
                Name = dto.Name.Trim(),
                BankName = dto.BankName?.Trim(),
                AccountNumberMasked = dto.AccountNumberMasked?.Trim(),
                CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? "USD" : dto.CurrencyCode.Trim().ToUpperInvariant(),
                GlAccountId = dto.GlAccountId,
                IsActive = dto.IsActive,
                BranchId = _user.BranchId,
            };
            _db.Add(b);
            await _db.SaveChangesAsync(ct);
            return await GetBankAccountAsync(b.Id, ct);
        }

        public async Task<BaseResponse<BankAccountDto>> UpdateBankAccountAsync(int id, BankAccountUpsertDto dto, CancellationToken ct = default)
        {
            var b = await _db.Set<BankAccount>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return new BaseResponse<BankAccountDto>(false, "Bank account not found", "");
            var err = await ValidateBankAccountAsync(dto, ct);
            if (err is not null) return new BaseResponse<BankAccountDto>(false, err, "");

            b.Name = dto.Name.Trim();
            b.BankName = dto.BankName?.Trim();
            b.AccountNumberMasked = dto.AccountNumberMasked?.Trim();
            b.CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? b.CurrencyCode : dto.CurrencyCode.Trim().ToUpperInvariant();
            b.GlAccountId = dto.GlAccountId;
            b.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return await GetBankAccountAsync(b.Id, ct);
        }

        public async Task<BaseResponse> DeleteBankAccountAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.Set<BankAccount>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return new BaseResponse(false, "Bank account not found");
            var hasStatements = await _db.Set<BankStatement>().AnyAsync(s => s.BankAccountId == id, ct);
            if (hasStatements)
            {
                b.IsActive = false; // keep history, just hide it
                await _db.SaveChangesAsync(ct);
                return new BaseResponse(true, null);
            }
            _db.Remove(b);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse(true, null);
        }

        private async Task<string?> ValidateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";
            var gl = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.GlAccountId, ct);
            if (gl is null || !gl.IsActive) return "GL account not found or inactive";
            return null;
        }

        // ============================================
        // STATEMENTS
        // ============================================

        public async Task<List<BankStatementDto>> ListStatementsAsync(int? bankAccountId, CancellationToken ct = default)
        {
            var q = _db.Set<BankStatement>().AsNoTracking().Include(s => s.Lines).AsQueryable();
            if (bankAccountId.HasValue) q = q.Where(s => s.BankAccountId == bankAccountId.Value);
            var rows = await q.OrderByDescending(s => s.PeriodTo).ThenByDescending(s => s.Id).ToListAsync(ct);
            var names = await _db.Set<BankAccount>().AsNoTracking()
                .Where(b => rows.Select(r => r.BankAccountId).Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            return rows.Select(s => ToDto(s, names.GetValueOrDefault(s.BankAccountId))).ToList();
        }

        public async Task<BaseResponse<BankStatementDto>> CreateStatementAsync(BankStatementCreateDto dto, CancellationToken ct = default)
        {
            var bank = await _db.Set<BankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == dto.BankAccountId, ct);
            if (bank is null) return new BaseResponse<BankStatementDto>(false, "Bank account not found", "");
            if (dto.PeriodTo < dto.PeriodFrom) return new BaseResponse<BankStatementDto>(false, "PeriodTo must be after PeriodFrom", "");

            var s = new BankStatement
            {
                BankAccountId = bank.Id,
                PeriodFrom = AcctDates.StartOfDay(dto.PeriodFrom),
                PeriodTo = AcctDates.EndOfDay(dto.PeriodTo),
                OpeningBalance = dto.OpeningBalance,
                ClosingBalance = dto.ClosingBalance,
                ImportedOn = DateTime.UtcNow,
                ImportedBy = _user.UserName,
                Status = "Open",
                BranchId = _user.BranchId,
            };
            foreach (var l in dto.Lines ?? new List<BankStatementLineCreateDto>())
            {
                s.Lines.Add(new BankStatementLine
                {
                    Date = AcctDates.StartOfDay(l.Date),
                    Description = l.Description?.Trim(),
                    Reference = l.Reference?.Trim(),
                    Amount = l.Amount,
                    MatchStatus = "Unmatched",
                });
            }
            _db.Add(s);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<BankStatementDto>(true, null, "Statement created", ToDto(s, bank.Name));
        }

        public async Task<BaseResponse<CsvImportResultDto>> ImportCsvAsync(BankStatementCreateDto header, string csvContent, CancellationToken ct = default)
        {
            var (lines, errors, skipped) = ParseCsv(csvContent);
            if (lines.Count == 0)
                return new BaseResponse<CsvImportResultDto>(false, errors.Count > 0 ? string.Join("; ", errors.Take(5)) : "No rows found in CSV", "");

            var created = await CreateStatementAsync(header with { Lines = lines }, ct);
            if (!created.Success || created.Data is null)
                return new BaseResponse<CsvImportResultDto>(false, created.Error, "");

            return new BaseResponse<CsvImportResultDto>(true, null, "Imported",
                new CsvImportResultDto(created.Data.Id, lines.Count, skipped, errors));
        }

        /// <summary>
        /// Parses "date,description,reference,amount" CSV (header optional, any column order
        /// when a header is present). Dates: dd/MM/yyyy or yyyy-MM-dd. Public static so tests can hit it.
        /// </summary>
        public static (List<BankStatementLineCreateDto> Lines, List<string> Errors, int Skipped) ParseCsv(string content)
        {
            var result = new List<BankStatementLineCreateDto>();
            var errors = new List<string>();
            var skipped = 0;
            if (string.IsNullOrWhiteSpace(content)) return (result, errors, skipped);

            var rawLines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (rawLines.Count == 0) return (result, errors, skipped);

            int iDate = 0, iDesc = 1, iRef = 2, iAmt = 3;
            var first = SplitCsv(rawLines[0]);
            var startRow = 0;
            if (first.Any(c => c.Equals("date", StringComparison.OrdinalIgnoreCase)) &&
                first.Any(c => c.Equals("amount", StringComparison.OrdinalIgnoreCase)))
            {
                iDate = Array.FindIndex(first, c => c.Equals("date", StringComparison.OrdinalIgnoreCase));
                iDesc = Array.FindIndex(first, c => c.Equals("description", StringComparison.OrdinalIgnoreCase));
                iRef = Array.FindIndex(first, c => c.Equals("reference", StringComparison.OrdinalIgnoreCase));
                iAmt = Array.FindIndex(first, c => c.Equals("amount", StringComparison.OrdinalIgnoreCase));
                startRow = 1;
            }

            var formats = new[] { "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "yyyy-M-d", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss" };
            for (var r = startRow; r < rawLines.Count; r++)
            {
                var cells = SplitCsv(rawLines[r]);
                if (cells.Length <= Math.Max(iDate, iAmt))
                {
                    errors.Add($"Row {r + 1}: not enough columns");
                    skipped++;
                    continue;
                }
                if (!DateTime.TryParseExact(cells[iDate].Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                {
                    errors.Add($"Row {r + 1}: invalid date '{cells[iDate]}'");
                    skipped++;
                    continue;
                }
                var amtText = cells[iAmt].Trim().Replace(",", "").Replace("$", "");
                if (amtText.StartsWith("(") && amtText.EndsWith(")")) amtText = "-" + amtText.Trim('(', ')');
                if (!decimal.TryParse(amtText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                {
                    errors.Add($"Row {r + 1}: invalid amount '{cells[iAmt]}'");
                    skipped++;
                    continue;
                }
                var desc = iDesc >= 0 && iDesc < cells.Length ? cells[iDesc].Trim() : null;
                var reference = iRef >= 0 && iRef < cells.Length ? cells[iRef].Trim() : null;
                result.Add(new BankStatementLineCreateDto(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc), desc, reference, Math.Round(amount, 2)));
            }
            return (result, errors, skipped);
        }

        private static string[] SplitCsv(string line)
        {
            var cells = new List<string>();
            var cur = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes) { cells.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            cells.Add(cur.ToString());
            return cells.ToArray();
        }

        public async Task<BaseResponse<BankStatementDetailDto>> GetStatementAsync(int statementId, string? matchStatus, CancellationToken ct = default)
        {
            var s = await _db.Set<BankStatement>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == statementId, ct);
            if (s is null) return new BaseResponse<BankStatementDetailDto>(false, "Statement not found", "");
            var bankName = await _db.Set<BankAccount>().AsNoTracking().Where(b => b.Id == s.BankAccountId).Select(b => b.Name).FirstOrDefaultAsync(ct);
            var lines = s.Lines.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(matchStatus))
                lines = lines.Where(l => l.MatchStatus.Equals(matchStatus, StringComparison.OrdinalIgnoreCase));
            var dto = new BankStatementDetailDto(ToDto(s, bankName), lines.OrderBy(l => l.Date).ThenBy(l => l.Id).Select(ToDto).ToList());
            return new BaseResponse<BankStatementDetailDto>(true, null, "", dto);
        }

        public async Task<BaseResponse> DeleteStatementAsync(int statementId, CancellationToken ct = default)
        {
            var s = await _db.Set<BankStatement>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == statementId, ct);
            if (s is null) return new BaseResponse(false, "Statement not found");
            if (s.Status == "Reconciled") return new BaseResponse(false, "Reconciled statements cannot be deleted");
            _db.RemoveRange(s.Lines);
            _db.Remove(s);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse(true, null);
        }

        // ============================================
        // MATCHING
        // ============================================

        public async Task<BaseResponse<List<BankMatchSuggestionDto>>> GetSuggestionsAsync(int statementId, int lineId, CancellationToken ct = default)
        {
            var ctx = await LoadLineContextAsync(statementId, lineId, ct);
            if (ctx.Error is not null) return new BaseResponse<List<BankMatchSuggestionDto>>(false, ctx.Error, "");
            var (s, line, bank) = (ctx.Statement!, ctx.Line!, ctx.Bank!);

            var from = line.Date.Date.AddDays(-SuggestionWindowDays);
            var to = line.Date.Date.AddDays(SuggestionWindowDays + 1);
            var abs = Math.Abs(line.Amount);

            var alreadyMatched = await _db.Set<BankStatementLine>().AsNoTracking()
                .Where(l => l.MatchedJournalEntryLineId != null && l.Id != line.Id)
                .Select(l => l.MatchedJournalEntryLineId!.Value)
                .ToListAsync(ct);

            var candidates = await _db.Set<JournalEntryLine>().AsNoTracking()
                .Include(l => l.JournalEntry)
                .Where(l => l.AccountId == bank.GlAccountId
                            && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided
                            && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate < to
                            && (l.DebitAmount == abs || l.CreditAmount == abs))
                .ToListAsync(ct);

            var list = candidates
                .Where(l => !alreadyMatched.Contains(l.Id))
                .Select(l => new BankMatchSuggestionDto(
                    l.Id, l.JournalEntryId, l.JournalEntry.EntryNumber, l.JournalEntry.EntryDate,
                    l.Description ?? l.JournalEntry.Description, l.DebitAmount, l.CreditAmount,
                    l.DebitAmount - l.CreditAmount,
                    Math.Abs((l.JournalEntry.EntryDate.Date - line.Date.Date).Days)))
                // same direction first (deposit ↔ debit on the asset account), then closest date
                .OrderBy(x => Math.Sign(x.SignedAmount) == Math.Sign(line.Amount) ? 0 : 1)
                .ThenBy(x => x.DayDifference)
                .ToList();

            return new BaseResponse<List<BankMatchSuggestionDto>>(true, null, "", list);
        }

        public async Task<BaseResponse<BankStatementLineDto>> MatchLineAsync(int statementId, int lineId, int journalEntryLineId, CancellationToken ct = default)
        {
            var ctx = await LoadLineContextAsync(statementId, lineId, ct, track: true);
            if (ctx.Error is not null) return new BaseResponse<BankStatementLineDto>(false, ctx.Error, "");
            var (s, line, bank) = (ctx.Statement!, ctx.Line!, ctx.Bank!);
            if (s.Status == "Reconciled") return new BaseResponse<BankStatementLineDto>(false, "Statement is already reconciled", "");

            var jl = await _db.Set<JournalEntryLine>().AsNoTracking().Include(l => l.JournalEntry).FirstOrDefaultAsync(l => l.Id == journalEntryLineId, ct);
            if (jl is null) return new BaseResponse<BankStatementLineDto>(false, "Journal entry line not found", "");
            if (jl.AccountId != bank.GlAccountId) return new BaseResponse<BankStatementLineDto>(false, "Journal line is not on this bank's GL account", "");
            if (!jl.JournalEntry.IsPosted || jl.JournalEntry.IsVoided) return new BaseResponse<BankStatementLineDto>(false, "Journal entry must be posted and not voided", "");

            var taken = await _db.Set<BankStatementLine>().AsNoTracking()
                .AnyAsync(l => l.MatchedJournalEntryLineId == journalEntryLineId && l.Id != line.Id, ct);
            if (taken) return new BaseResponse<BankStatementLineDto>(false, "That journal line is already matched to another statement line", "");

            line.MatchedJournalEntryLineId = journalEntryLineId;
            line.MatchStatus = "Matched";
            line.MatchedOn = DateTime.UtcNow;
            line.MatchedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<BankStatementLineDto>(true, null, "Matched", ToDto(line));
        }

        public async Task<BaseResponse<BankStatementLineDto>> UnmatchLineAsync(int statementId, int lineId, CancellationToken ct = default)
        {
            var ctx = await LoadLineContextAsync(statementId, lineId, ct, track: true);
            if (ctx.Error is not null) return new BaseResponse<BankStatementLineDto>(false, ctx.Error, "");
            if (ctx.Statement!.Status == "Reconciled") return new BaseResponse<BankStatementLineDto>(false, "Statement is already reconciled", "");
            var line = ctx.Line!;
            line.MatchedJournalEntryLineId = null;
            line.MatchStatus = "Unmatched";
            line.MatchedOn = null;
            line.MatchedBy = null;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<BankStatementLineDto>(true, null, "Unmatched", ToDto(line));
        }

        public async Task<BaseResponse<BankStatementLineDto>> ExcludeLineAsync(int statementId, int lineId, string? note, CancellationToken ct = default)
        {
            var ctx = await LoadLineContextAsync(statementId, lineId, ct, track: true);
            if (ctx.Error is not null) return new BaseResponse<BankStatementLineDto>(false, ctx.Error, "");
            if (ctx.Statement!.Status == "Reconciled") return new BaseResponse<BankStatementLineDto>(false, "Statement is already reconciled", "");
            var line = ctx.Line!;
            line.MatchedJournalEntryLineId = null;
            line.MatchStatus = "Excluded";
            line.MatchedOn = DateTime.UtcNow;
            line.MatchedBy = _user.UserName;
            line.Note = note?.Trim();
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<BankStatementLineDto>(true, null, "Excluded", ToDto(line));
        }

        // ============================================
        // SUMMARY / RECONCILE
        // ============================================

        public async Task<BaseResponse<BankReconciliationSummaryDto>> GetSummaryAsync(int statementId, CancellationToken ct = default)
        {
            var s = await _db.Set<BankStatement>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == statementId, ct);
            if (s is null) return new BaseResponse<BankReconciliationSummaryDto>(false, "Statement not found", "");
            var bank = await _db.Set<BankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == s.BankAccountId, ct);
            if (bank is null) return new BaseResponse<BankReconciliationSummaryDto>(false, "Bank account not found", "");
            return new BaseResponse<BankReconciliationSummaryDto>(true, null, "", await BuildSummaryAsync(s, bank, ct));
        }

        public async Task<BaseResponse<BankReconciliationSummaryDto>> ReconcileAsync(int statementId, CancellationToken ct = default)
        {
            var s = await _db.Set<BankStatement>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == statementId, ct);
            if (s is null) return new BaseResponse<BankReconciliationSummaryDto>(false, "Statement not found", "");
            if (s.Status == "Reconciled") return new BaseResponse<BankReconciliationSummaryDto>(false, "Statement is already reconciled", "");
            var bank = await _db.Set<BankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == s.BankAccountId, ct);
            if (bank is null) return new BaseResponse<BankReconciliationSummaryDto>(false, "Bank account not found", "");

            var open = await _periods.EnsureOpenAsync(s.PeriodTo, ct);
            if (!open.Success) return new BaseResponse<BankReconciliationSummaryDto>(false, open.Error, "");

            var summary = await BuildSummaryAsync(s, bank, ct);
            if (summary.UnmatchedCount > 0)
                return new BaseResponse<BankReconciliationSummaryDto>(false, $"{summary.UnmatchedCount} statement line(s) are still unmatched", "", summary);
            if (summary.Difference != 0)
                return new BaseResponse<BankReconciliationSummaryDto>(false, $"Closing balance differs from ledger by {summary.Difference:0.00}", "", summary);

            s.Status = "Reconciled";
            s.ReconciledOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Bank statement {Id} reconciled by {User}", s.Id, _user.UserName);
            return new BaseResponse<BankReconciliationSummaryDto>(true, null, "Reconciled", summary with { Status = s.Status, CanReconcile = false });
        }

        private async Task<BankReconciliationSummaryDto> BuildSummaryAsync(BankStatement s, BankAccount bank, CancellationToken ct)
        {
            var ledgerLines = await _db.Set<JournalEntryLine>().AsNoTracking()
                .Where(l => l.AccountId == bank.GlAccountId && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided && l.JournalEntry.EntryDate <= s.PeriodTo)
                .Select(l => new { l.Id, l.DebitAmount, l.CreditAmount, l.JournalEntry.EntryDate })
                .ToListAsync(ct);

            var ledgerBalance = ledgerLines.Sum(l => l.DebitAmount - l.CreditAmount);

            var matchedIds = s.Lines.Where(l => l.MatchedJournalEntryLineId.HasValue).Select(l => l.MatchedJournalEntryLineId!.Value).ToHashSet();
            var inPeriodUnmatched = ledgerLines.Where(l => l.EntryDate >= s.PeriodFrom && !matchedIds.Contains(l.Id)).ToList();

            var unmatched = s.Lines.Where(l => l.MatchStatus == "Unmatched").ToList();
            var matched = s.Lines.Where(l => l.MatchStatus == "Matched").ToList();
            var excluded = s.Lines.Count(l => l.MatchStatus == "Excluded");
            var difference = Math.Round(s.ClosingBalance - ledgerBalance, 2);

            return new BankReconciliationSummaryDto(
                s.Id, s.PeriodFrom, s.PeriodTo, s.OpeningBalance, s.ClosingBalance,
                Math.Round(ledgerBalance, 2), difference,
                s.Lines.Count, matched.Count, unmatched.Count, excluded,
                Math.Round(unmatched.Sum(l => l.Amount), 2), Math.Round(matched.Sum(l => l.Amount), 2),
                Math.Round(s.Lines.Where(l => l.MatchStatus != "Excluded").Sum(l => l.Amount), 2),
                inPeriodUnmatched.Count, Math.Round(inPeriodUnmatched.Sum(l => l.DebitAmount - l.CreditAmount), 2),
                s.Status != "Reconciled" && unmatched.Count == 0 && difference == 0, s.Status);
        }

        // ============================================
        // HELPERS
        // ============================================

        private sealed record LineContext(BankStatement? Statement, BankStatementLine? Line, BankAccount? Bank, string? Error);

        private async Task<LineContext> LoadLineContextAsync(int statementId, int lineId, CancellationToken ct, bool track = false)
        {
            var q = _db.Set<BankStatement>().Include(x => x.Lines).AsQueryable();
            if (!track) q = q.AsNoTracking();
            var s = await q.FirstOrDefaultAsync(x => x.Id == statementId, ct);
            if (s is null) return new LineContext(null, null, null, "Statement not found");
            var line = s.Lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null) return new LineContext(null, null, null, "Statement line not found");
            var bank = await _db.Set<BankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == s.BankAccountId, ct);
            if (bank is null) return new LineContext(null, null, null, "Bank account not found");
            return new LineContext(s, line, bank, null);
        }

        private async Task<Dictionary<int, Account>> LoadAccountsAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            var set = ids.Distinct().ToList();
            return await _db.Set<Account>().AsNoTracking().Where(a => set.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        }

        private static BankAccountDto ToDto(BankAccount b, Dictionary<int, Account> accts)
        {
            accts.TryGetValue(b.GlAccountId, out var gl);
            return new BankAccountDto(b.Id, b.Name, b.BankName, b.AccountNumberMasked, b.CurrencyCode, b.GlAccountId,
                gl?.AccountNumber, gl?.AccountName, b.IsActive, b.BranchId, b.CreatedOn);
        }

        private static BankStatementDto ToDto(BankStatement s, string? bankName) => new(
            s.Id, s.BankAccountId, bankName, s.PeriodFrom, s.PeriodTo, s.OpeningBalance, s.ClosingBalance, s.ImportedOn, s.ImportedBy,
            s.Status, s.ReconciledOn, s.Lines.Count, s.Lines.Count(l => l.MatchStatus == "Unmatched"));

        private static BankStatementLineDto ToDto(BankStatementLine l) => new(
            l.Id, l.StatementId, l.Date, l.Description, l.Reference, l.Amount, l.MatchedJournalEntryLineId, l.MatchStatus, l.MatchedOn, l.MatchedBy, l.Note);
    }
}
