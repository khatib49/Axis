using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    /// <summary>
    /// Owners and their drawings.
    ///
    /// Accounting treatment: a drawing is cash an owner takes out for personal
    /// use. It is a reduction of equity, not an expense, so it never touches
    /// profit. Chart of accounts:
    ///
    ///     3300  Owners' Drawings            (Equity, header, no direct posting)
    ///       3310  Drawings – Owner A        (Equity, one per owner)
    ///       3320  Drawings – Owner B
    ///
    /// Each drawing posts   DR 33x0 owner drawings / CR 1000 Cash on Hand.
    /// The header's rollup is the total of all drawings. Drawings accounts
    /// carry a debit balance against Equity's credit normal balance (contra
    /// equity), so the trial balance shows them in the Debit column.
    ///
    /// The header number is not hard-coded: it is remembered in
    /// IntegrationSettings and created on first use with the first free 3x00.
    /// </summary>
    public class OwnerService : IOwnerService
    {
        public const string ReferenceType = "OwnerDrawing";
        private const string HeaderSettingKey = "Accounting.OwnerDrawingsHeaderAccountId";
        private const string HeaderAccountName = "Owners' Drawings";
        private const string EquityType = "Equity";
        private const string CashAccountNumber = "1000";

        private readonly IBaseRepository<Owner> _ownerRepo;
        private readonly IBaseRepository<OwnerDrawing> _drawingRepo;
        private readonly IBaseRepository<Account> _accountRepo;
        private readonly IBaseRepository<AccountType> _accountTypeRepo;
        private readonly IBaseRepository<JournalEntry> _journalRepo;
        private readonly IBaseRepository<JournalEntryLine> _journalLineRepo;
        private readonly IBaseRepository<ExpenseCategory> _expenseCategoryRepo;
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<IntegrationSetting> _settingsRepo;
        private readonly IJournalService _journal;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<OwnerService> _logger;

        public OwnerService(
            IBaseRepository<Owner> ownerRepo,
            IBaseRepository<OwnerDrawing> drawingRepo,
            IBaseRepository<Account> accountRepo,
            IBaseRepository<AccountType> accountTypeRepo,
            IBaseRepository<JournalEntry> journalRepo,
            IBaseRepository<JournalEntryLine> journalLineRepo,
            IBaseRepository<ExpenseCategory> expenseCategoryRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<IntegrationSetting> settingsRepo,
            IJournalService journal,
            IUnitOfWork uow,
            ILogger<OwnerService> logger)
        {
            _ownerRepo = ownerRepo;
            _drawingRepo = drawingRepo;
            _accountRepo = accountRepo;
            _accountTypeRepo = accountTypeRepo;
            _journalRepo = journalRepo;
            _journalLineRepo = journalLineRepo;
            _expenseCategoryRepo = expenseCategoryRepo;
            _expenseRepo = expenseRepo;
            _settingsRepo = settingsRepo;
            _journal = journal;
            _uow = uow;
            _logger = logger;
        }

        // ============================================
        // OWNERS
        // ============================================

        public async Task<IReadOnlyList<OwnerDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _ownerRepo.Query();
            if (!includeInactive)
                q = q.Where(o => o.IsActive);

            return await q
                .OrderByDescending(o => o.OwnershipPercent)
                .ThenBy(o => o.Name)
                .Select(o => new OwnerDto(
                    o.Id, o.Name, o.OwnershipPercent, o.DrawingsAccountId,
                    o.DrawingsAccount.AccountNumber, o.DrawingsAccount.AccountName,
                    o.Notes, o.IsActive, o.CreatedOn, o.ModifiedOn))
                .ToListAsync(ct);
        }

        public async Task<OwnerDto?> GetAsync(int id, CancellationToken ct = default)
        {
            return await _ownerRepo.Query()
                .Where(o => o.Id == id)
                .Select(o => new OwnerDto(
                    o.Id, o.Name, o.OwnershipPercent, o.DrawingsAccountId,
                    o.DrawingsAccount.AccountNumber, o.DrawingsAccount.AccountName,
                    o.Notes, o.IsActive, o.CreatedOn, o.ModifiedOn))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<OwnerDto> CreateAsync(OwnerCreateDto dto, int? userId, CancellationToken ct = default)
        {
            var name = (dto.Name ?? "").Trim();
            if (name.Length < 2)
                throw new ArgumentException("Owner name is required.");
            ValidatePercent(dto.OwnershipPercent);

            if (await _ownerRepo.Query().AnyAsync(o => o.Name.ToLower() == name.ToLower(), ct))
                throw new InvalidOperationException($"An owner named '{name}' already exists.");

            await EnsureTotalOwnershipAsync(dto.OwnershipPercent, excludeOwnerId: null, ct);

            await _uow.BeginTransactionAsync(ct);
            try
            {
                var header = await EnsureHeaderAsync(userId, ct);

                Account account;
                if (dto.ExistingAccountId.HasValue)
                {
                    account = await _accountRepo.Query(asNoTracking: false)
                        .Include(a => a.AccountType)
                        .FirstOrDefaultAsync(a => a.Id == dto.ExistingAccountId.Value, ct)
                        ?? throw new ArgumentException("The selected account was not found.");

                    if (!account.IsActive)
                        throw new ArgumentException($"Account {account.AccountNumber} is inactive.");
                    if (!string.Equals(account.AccountType.TypeName, EquityType, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException($"Account {account.AccountNumber} is a {account.AccountType.TypeName} account. Drawings must be an Equity account.");
                    if (account.Id == header.Id)
                        throw new ArgumentException("The Owners' Drawings header itself cannot be an owner's account.");
                    if (!account.AllowManualEntry || account.IsSystemAccount)
                        throw new ArgumentException($"Account {account.AccountNumber} is a header/system account and cannot hold an owner's drawings.");
                    if (await _ownerRepo.Query().AnyAsync(o => o.DrawingsAccountId == account.Id, ct))
                        throw new InvalidOperationException($"Account {account.AccountNumber} is already linked to another owner.");

                    // Move it under the header so the header rolls it up.
                    account.ParentAccountId = header.Id;
                    account.ModifiedAt = DateTime.UtcNow;
                    account.ModifiedBy = userId;
                }
                else
                {
                    account = new Account
                    {
                        AccountNumber = await NextChildNumberAsync(header, ct),
                        AccountName = AccountNameFor(name),
                        AccountTypeId = header.AccountTypeId,
                        ParentAccountId = header.Id,
                        Description = DescriptionFor(name, dto.OwnershipPercent),
                        CurrentBalance = 0,
                        IsActive = true,
                        IsSystemAccount = false,
                        AllowManualEntry = true,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = userId
                    };
                    await _accountRepo.AddAsync(account, ct);
                }
                await _uow.SaveChangesAsync(ct);

                var owner = new Owner
                {
                    Name = name,
                    OwnershipPercent = Math.Round(dto.OwnershipPercent, 2),
                    DrawingsAccountId = account.Id,
                    Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                    IsActive = true,
                    CreatedOn = DateTime.UtcNow
                };
                await _ownerRepo.AddAsync(owner, ct);
                await _uow.SaveChangesAsync(ct);
                await _uow.CommitAsync(ct);

                _logger.LogInformation("Owner {Name} ({Pct}%) created with drawings account {Acc}",
                    owner.Name, owner.OwnershipPercent, account.AccountNumber);

                return (await GetAsync(owner.Id, ct))!;
            }
            catch
            {
                await _uow.RollbackAsync(ct);
                _uow.ResetChangeTracker();
                throw;
            }
        }

        public async Task<OwnerDto> UpdateAsync(int id, OwnerUpdateDto dto, int? userId, CancellationToken ct = default)
        {
            var name = (dto.Name ?? "").Trim();
            if (name.Length < 2)
                throw new ArgumentException("Owner name is required.");
            ValidatePercent(dto.OwnershipPercent);

            var owner = await _ownerRepo.Query(asNoTracking: false)
                .Include(o => o.DrawingsAccount)
                .FirstOrDefaultAsync(o => o.Id == id, ct)
                ?? throw new KeyNotFoundException("Owner not found.");

            if (await _ownerRepo.Query().AnyAsync(o => o.Id != id && o.Name.ToLower() == name.ToLower(), ct))
                throw new InvalidOperationException($"Another owner named '{name}' already exists.");

            if (dto.IsActive)
                await EnsureTotalOwnershipAsync(dto.OwnershipPercent, excludeOwnerId: id, ct);

            // Keep the auto-generated account name in step with a rename. A
            // linked legacy account with its own name is left alone.
            var account = owner.DrawingsAccount;
            if (account.AccountName == AccountNameFor(owner.Name) && owner.Name != name)
            {
                account.AccountName = AccountNameFor(name);
                account.ModifiedAt = DateTime.UtcNow;
                account.ModifiedBy = userId;
            }
            if (account.Description == DescriptionFor(owner.Name, owner.OwnershipPercent))
                account.Description = DescriptionFor(name, dto.OwnershipPercent);

            owner.Name = name;
            owner.OwnershipPercent = Math.Round(dto.OwnershipPercent, 2);
            owner.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            owner.IsActive = dto.IsActive;
            owner.ModifiedOn = DateTime.UtcNow;

            await _uow.SaveChangesAsync(ct);
            return (await GetAsync(id, ct))!;
        }

        public async Task<bool> DeactivateAsync(int id, CancellationToken ct = default)
        {
            var owner = await _ownerRepo.Query(asNoTracking: false).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (owner == null || !owner.IsActive) return false;

            owner.IsActive = false;
            owner.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        // ============================================
        // DRAWINGS
        // ============================================

        public async Task<PagedOwnerDrawingsResult> QueryDrawingsAsync(OwnerDrawingFilter filter, CancellationToken ct = default)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 500);

            var q = _drawingRepo.Query();
            if (!filter.IncludeVoided)
                q = q.Where(d => !d.IsVoided);
            if (filter.OwnerId.HasValue)
                q = q.Where(d => d.OwnerId == filter.OwnerId.Value);
            if (filter.From.HasValue)
            {
                var from = Utc(filter.From.Value.Date);
                q = q.Where(d => d.DrawingDate >= from);
            }
            if (filter.To.HasValue)
            {
                var toExclusive = Utc(filter.To.Value.Date.AddDays(1));
                q = q.Where(d => d.DrawingDate < toExclusive);
            }

            var totalCount = await q.CountAsync(ct);
            var totalAll = await q.Where(d => !d.IsVoided).SumAsync(d => (decimal?)d.Amount, ct) ?? 0m;

            var rows = await q
                .OrderByDescending(d => d.DrawingDate)
                .ThenByDescending(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new { d, OwnerName = d.Owner.Name })
                .ToListAsync(ct);

            var entryIds = rows.Where(r => r.d.JournalEntryId.HasValue).Select(r => r.d.JournalEntryId!.Value).ToList();
            var entryNumbers = await _journalRepo.Query()
                .Where(j => entryIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id, j => j.EntryNumber, ct);

            var items = rows.Select(r => ToDto(r.d, r.OwnerName,
                r.d.JournalEntryId.HasValue && entryNumbers.TryGetValue(r.d.JournalEntryId.Value, out var n) ? n : null)).ToList();
            return new PagedOwnerDrawingsResult(page, pageSize, totalCount, totalAll, items);
        }

        public async Task<OwnerDrawingDto> CreateDrawingAsync(OwnerDrawingCreateDto dto, int? userId, CancellationToken ct = default)
        {
            var owner = await LoadActiveOwnerAsync(dto.OwnerId, ct);
            ValidateDrawing(dto.Amount, dto.DrawingDate);

            await _uow.BeginTransactionAsync(ct);
            try
            {
                var drawing = new OwnerDrawing
                {
                    OwnerId = owner.Id,
                    Amount = Math.Round(dto.Amount, 2),
                    DrawingDate = Utc(dto.DrawingDate.Date),
                    PaymentMethod = Clean(dto.PaymentMethod, 50),
                    Comment = Clean(dto.Comment),
                    CreatedBy = userId,
                    CreatedOn = DateTime.UtcNow
                };
                await _drawingRepo.AddAsync(drawing, ct);
                await _uow.SaveChangesAsync(ct);

                var entry = await PostDrawingEntryAsync(drawing, owner, userId, ct);
                drawing.JournalEntryId = entry.Id;
                await _uow.SaveChangesAsync(ct);
                await _uow.CommitAsync(ct);

                _logger.LogInformation("Owner drawing {Id}: {Owner} took {Amount} ({Entry})",
                    drawing.Id, owner.Name, drawing.Amount, entry.EntryNumber);

                return ToDto(drawing, owner.Name, entry.EntryNumber);
            }
            catch
            {
                await _uow.RollbackAsync(ct);
                _uow.ResetChangeTracker();
                throw;
            }
        }

        /// <summary>
        /// Posted entries are locked, so an edit voids the old journal entry
        /// and posts a fresh one. Both stay in the ledger history.
        /// </summary>
        public async Task<OwnerDrawingDto> UpdateDrawingAsync(int id, OwnerDrawingUpdateDto dto, int? userId, CancellationToken ct = default)
        {
            var drawing = await _drawingRepo.Query(asNoTracking: false).FirstOrDefaultAsync(d => d.Id == id, ct)
                ?? throw new KeyNotFoundException("Drawing not found.");
            if (drawing.IsVoided)
                throw new InvalidOperationException("This drawing was cancelled and can no longer be edited.");

            var owner = drawing.OwnerId == dto.OwnerId
                ? await _ownerRepo.Query().Include(o => o.DrawingsAccount).FirstAsync(o => o.Id == dto.OwnerId, ct)
                : await LoadActiveOwnerAsync(dto.OwnerId, ct);
            ValidateDrawing(dto.Amount, dto.DrawingDate);

            await _uow.BeginTransactionAsync(ct);
            try
            {
                if (drawing.JournalEntryId.HasValue)
                    await VoidEntryAsync(drawing.JournalEntryId.Value, "Owner drawing edited", userId, ct);

                drawing.OwnerId = owner.Id;
                drawing.Amount = Math.Round(dto.Amount, 2);
                drawing.DrawingDate = Utc(dto.DrawingDate.Date);
                drawing.PaymentMethod = Clean(dto.PaymentMethod, 50);
                drawing.Comment = Clean(dto.Comment);
                drawing.ModifiedOn = DateTime.UtcNow;

                var entry = await PostDrawingEntryAsync(drawing, owner, userId, ct);
                drawing.JournalEntryId = entry.Id;
                await _uow.SaveChangesAsync(ct);
                await _uow.CommitAsync(ct);

                return ToDto(drawing, owner.Name, entry.EntryNumber);
            }
            catch
            {
                await _uow.RollbackAsync(ct);
                _uow.ResetChangeTracker();
                throw;
            }
        }

        public async Task<OwnerDrawingDto> VoidDrawingAsync(int id, string? reason, int? userId, CancellationToken ct = default)
        {
            var drawing = await _drawingRepo.Query(asNoTracking: false)
                .Include(d => d.Owner)
                .FirstOrDefaultAsync(d => d.Id == id, ct)
                ?? throw new KeyNotFoundException("Drawing not found.");
            if (drawing.IsVoided)
                throw new InvalidOperationException("This drawing is already cancelled.");

            var why = string.IsNullOrWhiteSpace(reason) ? "Owner drawing cancelled" : reason.Trim();

            await _uow.BeginTransactionAsync(ct);
            try
            {
                if (drawing.JournalEntryId.HasValue)
                    await VoidEntryAsync(drawing.JournalEntryId.Value, why, userId, ct);

                drawing.IsVoided = true;
                drawing.VoidedOn = DateTime.UtcNow;
                drawing.VoidedBy = userId;
                drawing.VoidReason = why.Length > 500 ? why[..500] : why;
                await _uow.SaveChangesAsync(ct);
                await _uow.CommitAsync(ct);
            }
            catch
            {
                await _uow.RollbackAsync(ct);
                _uow.ResetChangeTracker();
                throw;
            }

            var number = drawing.JournalEntryId.HasValue
                ? await _journalRepo.Query().Where(j => j.Id == drawing.JournalEntryId.Value).Select(j => j.EntryNumber).FirstOrDefaultAsync(ct)
                : null;
            return ToDto(drawing, drawing.Owner.Name, number);
        }

        public async Task<decimal> GetLifetimeDrawingsCashOutAsync(CancellationToken ct = default)
        {
            return Math.Round(await _drawingRepo.Query()
                .Where(d => !d.IsVoided)
                .SumAsync(d => (decimal?)d.Amount, ct) ?? 0m, 2);
        }

        // ============================================
        // SUMMARY (ledger based)
        // ============================================

        public async Task<OwnerDrawingsSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var header = await FindHeaderAsync(ct);
            var owners = await _ownerRepo.Query().ToListAsync(ct);

            if (header == null)
            {
                return new OwnerDrawingsSummaryDto(from, to, 0, "", HeaderAccountName, 0m, 0m,
                    owners.Where(o => o.IsActive).Sum(o => o.OwnershipPercent),
                    new List<OwnerDrawingsLineDto>(), new List<OwnerDrawingsLineDto>(),
                    await GetUnlinkedEquityCategoriesAsync(new HashSet<int>(), ct));
            }

            // Every account in the header's subtree (header included).
            var allAccounts = await _accountRepo.Query()
                .Select(a => new { a.Id, a.ParentAccountId, a.AccountNumber, a.AccountName, a.IsActive })
                .ToListAsync(ct);
            var childrenOf = allAccounts
                .Where(a => a.ParentAccountId.HasValue)
                .GroupBy(a => a.ParentAccountId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());
            var byId = allAccounts.ToDictionary(a => a.Id);

            HashSet<int> Subtree(int rootId)
            {
                var set = new HashSet<int>();
                var stack = new Stack<int>();
                stack.Push(rootId);
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    if (!set.Add(id)) continue;
                    if (childrenOf.TryGetValue(id, out var kids))
                        foreach (var k in kids) stack.Push(k);
                }
                return set;
            }

            var headerTree = Subtree(header.Id);
            var treeIds = headerTree.ToList();

            // Drawn = debits − credits (drawings are debit-natured).
            var linesQ = _journalLineRepo.Query()
                .Where(l => treeIds.Contains(l.AccountId)
                         && l.JournalEntry.IsPosted
                         && !l.JournalEntry.IsVoided);

            var lifetime = await linesQ
                .GroupBy(l => l.AccountId)
                .Select(g => new { AccountId = g.Key, Net = g.Sum(l => l.DebitAmount - l.CreditAmount) })
                .ToDictionaryAsync(x => x.AccountId, x => x.Net, ct);

            var periodQ = linesQ;
            if (from.HasValue)
            {
                var f = Utc(from.Value.Date);
                periodQ = periodQ.Where(l => l.JournalEntry.EntryDate >= f);
            }
            if (to.HasValue)
            {
                var t = Utc(to.Value.Date.AddDays(1));
                periodQ = periodQ.Where(l => l.JournalEntry.EntryDate < t);
            }
            var periodRows = await periodQ
                .Select(l => new { l.AccountId, l.JournalEntryId, Net = l.DebitAmount - l.CreditAmount })
                .ToListAsync(ct);

            var period = periodRows
                .GroupBy(r => r.AccountId)
                .ToDictionary(g => g.Key, g => (Net: g.Sum(r => r.Net), Entries: g.Select(r => r.JournalEntryId).ToHashSet()));

            (decimal drawn, int count, decimal life) SumOver(IEnumerable<int> ids)
            {
                decimal drawn = 0m, life = 0m;
                var entries = new HashSet<int>();
                foreach (var id in ids)
                {
                    if (period.TryGetValue(id, out var p)) { drawn += p.Net; entries.UnionWith(p.Entries); }
                    if (lifetime.TryGetValue(id, out var l)) life += l;
                }
                return (Math.Round(drawn, 2), entries.Count, Math.Round(life, 2));
            }

            var totalPeriod = SumOver(headerTree).drawn;
            var totalLifetime = SumOver(headerTree).life;

            OwnerDrawingsLineDto Line(int? ownerId, string name, decimal pct, int accountId, bool active, (decimal drawn, int count, decimal life) s)
            {
                var acc = byId[accountId];
                var entitled = Math.Round(totalPeriod * pct / 100m, 2);
                var share = totalPeriod != 0 ? Math.Round(s.drawn / totalPeriod * 100m, 2) : 0m;
                return new OwnerDrawingsLineDto(ownerId, name, pct, accountId, acc.AccountNumber, acc.AccountName,
                    active, s.drawn, s.count, share, entitled, Math.Round(s.drawn - entitled, 2), s.life);
            }

            // Owners whose account sits inside the header tree.
            var covered = new HashSet<int> { header.Id };
            var ownerLines = new List<OwnerDrawingsLineDto>();
            foreach (var o in owners.OrderByDescending(o => o.OwnershipPercent).ThenBy(o => o.Name))
            {
                if (!byId.ContainsKey(o.DrawingsAccountId)) continue;
                var tree = Subtree(o.DrawingsAccountId);
                covered.UnionWith(tree);
                var s = SumOver(tree);
                // Hidden owners only appear while they still have history.
                if (!o.IsActive && s.life == 0m) continue;
                ownerLines.Add(Line(o.Id, o.Name, o.IsActive ? o.OwnershipPercent : 0m, o.DrawingsAccountId, o.IsActive, s));
            }

            // Anything else under the header (accounts added by hand, or
            // postings on the header itself) so the rows always add up.
            var otherLines = new List<OwnerDrawingsLineDto>();
            foreach (var id in headerTree.Where(id => !covered.Contains(id)).OrderBy(id => byId[id].AccountNumber))
            {
                var s = SumOver(new[] { id });
                if (s.life == 0m && s.drawn == 0m) continue;
                otherLines.Add(Line(null, byId[id].AccountName, 0m, id, byId[id].IsActive, s));
            }
            var headerDirect = SumOver(new[] { header.Id });
            if (headerDirect.life != 0m || headerDirect.drawn != 0m)
                otherLines.Add(Line(null, $"{header.AccountName} (posted directly to header)", 0m, header.Id, true, headerDirect));

            return new OwnerDrawingsSummaryDto(
                from, to,
                header.Id, header.AccountNumber, header.AccountName,
                totalPeriod, totalLifetime,
                owners.Where(o => o.IsActive).Sum(o => o.OwnershipPercent),
                ownerLines, otherLines,
                await GetUnlinkedEquityCategoriesAsync(headerTree, ct));
        }

        public async Task<OwnerDrawingsLedgerDto> GetAccountLedgerAsync(int accountId, DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var header = await FindHeaderAsync(ct)
                ?? throw new KeyNotFoundException("The Owners' Drawings header account does not exist yet.");

            var accounts = await _accountRepo.Query()
                .Select(a => new { a.Id, a.ParentAccountId, a.AccountNumber, a.AccountName })
                .ToListAsync(ct);
            var account = accounts.FirstOrDefault(a => a.Id == accountId)
                ?? throw new KeyNotFoundException("Account not found.");

            var childrenOf = accounts
                .Where(a => a.ParentAccountId.HasValue)
                .GroupBy(a => a.ParentAccountId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());
            HashSet<int> Subtree(int rootId)
            {
                var set = new HashSet<int>();
                var stack = new Stack<int>();
                stack.Push(rootId);
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    if (!set.Add(id)) continue;
                    if (childrenOf.TryGetValue(id, out var kids))
                        foreach (var k in kids) stack.Push(k);
                }
                return set;
            }

            // Only accounts under the header — this is not a general ledger
            // endpoint. Same scope as the summary row: an owner's account
            // with anything below it; the header itself = its direct postings.
            if (!Subtree(header.Id).Contains(accountId))
                throw new ArgumentException($"Account {account.AccountNumber} is not under {header.AccountNumber} {header.AccountName}.");
            var ids = (accountId == header.Id ? new HashSet<int> { header.Id } : Subtree(accountId)).ToList();

            var q = _journalLineRepo.Query()
                .Where(l => ids.Contains(l.AccountId) && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided);
            if (from.HasValue)
            {
                var f = Utc(from.Value.Date);
                q = q.Where(l => l.JournalEntry.EntryDate >= f);
            }
            if (to.HasValue)
            {
                var t = Utc(to.Value.Date.AddDays(1));
                q = q.Where(l => l.JournalEntry.EntryDate < t);
            }

            var rows = await q
                .OrderBy(l => l.JournalEntry.EntryDate)
                .ThenBy(l => l.JournalEntry.EntryNumber)
                .ThenBy(l => l.LineNumber)
                .Select(l => new
                {
                    l.JournalEntryId,
                    l.JournalEntry.EntryNumber,
                    l.JournalEntry.EntryDate,
                    EntryDescription = l.JournalEntry.Description,
                    LineDescription = l.Description,
                    l.JournalEntry.ReferenceType,
                    l.JournalEntry.ReferenceId,
                    l.DebitAmount,
                    l.CreditAmount
                })
                .ToListAsync(ct);

            // Where each entry came from.
            var drawingIds = rows.Where(r => r.ReferenceType == ReferenceType && r.ReferenceId.HasValue)
                .Select(r => r.ReferenceId!.Value).Distinct().ToList();
            var drawings = await _drawingRepo.Query()
                .Where(d => drawingIds.Contains(d.Id))
                .Select(d => new { d.Id, d.PaymentMethod, d.Comment })
                .ToDictionaryAsync(d => d.Id, ct);

            var expenseIds = rows.Where(r => r.ReferenceType == "Expense" && r.ReferenceId.HasValue)
                .Select(r => r.ReferenceId!.Value).Distinct().ToList();
            var expenses = await _expenseRepo.Query()
                .Where(e => expenseIds.Contains(e.Id))
                .Select(e => new { e.Id, Category = e.Category.Name, e.Amount, e.FromDate, e.ToDate, e.PaymentMethod, e.Comment })
                .ToDictionaryAsync(e => e.Id, ct);

            static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

            var running = 0m;
            var lines = new List<OwnerDrawingsLedgerLineDto>(rows.Count);
            foreach (var r in rows)
            {
                running += r.DebitAmount - r.CreditAmount;
                string source;
                string? detail = null;
                int? drawingId = null, expenseId = null;

                if (r.ReferenceType == ReferenceType)
                {
                    source = "Drawings page";
                    drawingId = r.ReferenceId;
                    if (r.ReferenceId.HasValue && drawings.TryGetValue(r.ReferenceId.Value, out var d))
                        detail = Join(d.PaymentMethod, d.Comment);
                }
                else if (r.ReferenceType == "Expense")
                {
                    source = "Entry category";
                    expenseId = r.ReferenceId;
                    if (r.ReferenceId.HasValue && expenses.TryGetValue(r.ReferenceId.Value, out var e))
                    {
                        // A multi-month entry is booked as one journal entry per month.
                        var spread = e.FromDate.Date == e.ToDate.Date
                            ? null
                            : $"part of {e.Amount:#,0.00} spread {e.FromDate:dd MMM yyyy} – {e.ToDate:dd MMM yyyy}";
                        detail = Join(e.Category, e.PaymentMethod, e.Comment, spread);
                    }
                }
                else if (string.Equals(r.ReferenceType, "Adjustment", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(r.ReferenceType))
                {
                    source = "Manual journal entry";
                    detail = r.LineDescription;
                }
                else
                {
                    source = r.ReferenceType;
                    detail = r.LineDescription;
                }

                lines.Add(new OwnerDrawingsLedgerLineDto(
                    r.JournalEntryId, r.EntryNumber, r.EntryDate, r.EntryDescription, source, detail,
                    r.DebitAmount, r.CreditAmount, Math.Round(running, 2), drawingId, expenseId));
            }

            var owner = await _ownerRepo.Query()
                .Where(o => o.DrawingsAccountId == accountId)
                .Select(o => new { o.Id, o.Name })
                .FirstOrDefaultAsync(ct);

            var totalDebit = Math.Round(rows.Sum(r => r.DebitAmount), 2);
            var totalCredit = Math.Round(rows.Sum(r => r.CreditAmount), 2);
            return new OwnerDrawingsLedgerDto(
                account.Id, account.AccountNumber, account.AccountName,
                owner?.Id, owner?.Name, from, to,
                totalDebit, totalCredit, Math.Round(totalDebit - totalCredit, 2),
                rows.Select(r => r.JournalEntryId).Distinct().Count(),
                lines);
        }

        private async Task<List<UnlinkedEquityCategoryDto>> GetUnlinkedEquityCategoriesAsync(HashSet<int> headerTree, CancellationToken ct)
        {
            var cats = (await _expenseCategoryRepo.Query()
                .Where(c => c.Account != null && c.Account.AccountType.TypeName == EquityType)
                .Select(c => new { c.Id, c.Name, AccountId = c.Account!.Id, c.Account.AccountNumber, c.Account.AccountName })
                .ToListAsync(ct))
                .Where(c => !headerTree.Contains(c.AccountId))
                .ToList();
            if (cats.Count == 0) return new List<UnlinkedEquityCategoryDto>();

            var catIds = cats.Select(c => c.Id).ToList();
            var totals = await _expenseRepo.Query()
                .Where(e => catIds.Contains(e.FK_CategoryId))
                .GroupBy(e => e.FK_CategoryId)
                .Select(g => new { CategoryId = g.Key, Total = g.Sum(e => e.Amount), Count = g.Count() })
                .ToDictionaryAsync(x => x.CategoryId, ct);

            return cats
                .Select(c => totals.TryGetValue(c.Id, out var t)
                    ? new UnlinkedEquityCategoryDto(c.Id, c.Name, c.AccountNumber, c.AccountName, t.Total, t.Count)
                    : new UnlinkedEquityCategoryDto(c.Id, c.Name, c.AccountNumber, c.AccountName, 0m, 0))
                .OrderByDescending(c => c.TotalAmount)
                .ToList();
        }

        // ============================================
        // POSTING
        // ============================================

        /// <summary>DR owner's drawings account / CR 1000 Cash. Posted immediately.</summary>
        private async Task<JournalEntry> PostDrawingEntryAsync(OwnerDrawing drawing, Owner owner, int? userId, CancellationToken ct)
        {
            var drawingsAccount = await _accountRepo.Query(asNoTracking: false)
                .Include(a => a.AccountType)
                .FirstOrDefaultAsync(a => a.Id == owner.DrawingsAccountId, ct)
                ?? throw new InvalidOperationException($"Drawings account for {owner.Name} not found.");
            if (!drawingsAccount.IsActive)
                throw new InvalidOperationException($"Drawings account {drawingsAccount.AccountNumber} is inactive.");

            var cash = await _accountRepo.Query(asNoTracking: false)
                .Include(a => a.AccountType)
                .FirstOrDefaultAsync(a => a.AccountNumber == CashAccountNumber && a.IsActive, ct)
                ?? throw new InvalidOperationException("Cash account (1000) not found or inactive.");

            var description = string.IsNullOrWhiteSpace(drawing.Comment)
                ? $"Owner drawing – {owner.Name}"
                : $"Owner drawing – {owner.Name}: {drawing.Comment}";
            if (description.Length > 1000) description = description[..1000];
            var lineDescription = description.Length > 500 ? description[..500] : description;

            var entry = new JournalEntry
            {
                EntryNumber = await _journal.GenerateEntryNumberAsync(ct),
                EntryDate = drawing.DrawingDate,
                Description = description,
                ReferenceType = ReferenceType,
                ReferenceId = drawing.Id,
                TotalAmount = drawing.Amount,
                IsPosted = true,
                PostedAt = DateTime.UtcNow,
                PostedBy = userId,
                IsVoided = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            entry.Lines.Add(new JournalEntryLine
            {
                AccountId = drawingsAccount.Id,
                DebitAmount = drawing.Amount,
                CreditAmount = 0,
                Description = lineDescription,
                LineNumber = 1,
                CreatedAt = DateTime.UtcNow
            });
            entry.Lines.Add(new JournalEntryLine
            {
                AccountId = cash.Id,
                DebitAmount = 0,
                CreditAmount = drawing.Amount,
                Description = $"Cash paid to {owner.Name}",
                LineNumber = 2,
                CreatedAt = DateTime.UtcNow
            });
            await _journalRepo.AddAsync(entry, ct);

            // Cached balances, signed by each account's normal balance.
            drawingsAccount.CurrentBalance += Effect(drawingsAccount, drawing.Amount, 0);
            cash.CurrentBalance += Effect(cash, 0, drawing.Amount);

            await _uow.SaveChangesAsync(ct);
            return entry;
        }

        /// <summary>
        /// Marks the entry voided and rolls back its effect on cached balances.
        /// No reversing entry is written: every ledger read already excludes
        /// voided entries, so a reversal on top would cancel the amount twice.
        /// </summary>
        private async Task VoidEntryAsync(int entryId, string reason, int? userId, CancellationToken ct)
        {
            var entry = await _journalRepo.Query(asNoTracking: false)
                .Include(e => e.Lines).ThenInclude(l => l.Account).ThenInclude(a => a.AccountType)
                .FirstOrDefaultAsync(e => e.Id == entryId, ct);
            if (entry == null || entry.IsVoided) return;

            if (entry.IsPosted)
            {
                foreach (var line in entry.Lines)
                    line.Account.CurrentBalance -= Effect(line.Account, line.DebitAmount, line.CreditAmount);
            }

            entry.IsVoided = true;
            entry.VoidedAt = DateTime.UtcNow;
            entry.VoidedBy = userId;
            entry.VoidReason = reason.Length > 500 ? reason[..500] : reason;
            entry.ModifiedAt = DateTime.UtcNow;
            entry.ModifiedBy = userId;
            await _uow.SaveChangesAsync(ct);
        }

        // ============================================
        // HEADER ACCOUNT
        // ============================================

        /// <summary>The Owners' Drawings header, without creating it.</summary>
        private async Task<Account?> FindHeaderAsync(CancellationToken ct)
        {
            var raw = await _settingsRepo.Query()
                .Where(s => s.Key == HeaderSettingKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(ct);

            if (int.TryParse(raw, out var id))
            {
                var byId = await _accountRepo.Query()
                    .FirstOrDefaultAsync(a => a.Id == id && a.IsActive && a.AccountType.TypeName == EquityType, ct);
                if (byId != null) return byId;
            }

            return await _accountRepo.Query()
                .Where(a => a.IsActive && a.AccountType.TypeName == EquityType
                         && a.AccountName.ToLower() == HeaderAccountName.ToLower())
                .OrderBy(a => a.AccountNumber)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Finds or creates the header (first free of 3300, 3400 … 3900) and
        /// remembers its id. Must run inside the caller's transaction.
        /// </summary>
        private async Task<Account> EnsureHeaderAsync(int? userId, CancellationToken ct)
        {
            var header = await FindHeaderAsync(ct);

            if (header == null)
            {
                var equity = await _accountTypeRepo.Query()
                    .FirstOrDefaultAsync(t => t.TypeName == EquityType, ct)
                    ?? throw new InvalidOperationException("The chart of accounts has no \"Equity\" account type.");

                var taken = await _accountRepo.Query()
                    .Where(a => a.AccountNumber.StartsWith("3"))
                    .Select(a => a.AccountNumber)
                    .ToListAsync(ct);
                var number = new[] { "3300", "3400", "3500", "3600", "3700", "3800", "3900" }
                    .FirstOrDefault(n => !taken.Contains(n))
                    ?? throw new InvalidOperationException("No free 3x00 account number for the Owners' Drawings header. Create it manually in the Chart of Accounts (Equity, no manual entry) and name it \"Owners' Drawings\".");

                header = new Account
                {
                    AccountNumber = number,
                    AccountName = HeaderAccountName,
                    AccountTypeId = equity.Id,
                    ParentAccountId = null,
                    Description = "Total cash taken out by the owners for personal use. Contra-equity: reduces owners' equity, never an expense. Each owner has a sub-account below.",
                    CurrentBalance = 0,
                    IsActive = true,
                    IsSystemAccount = true,
                    AllowManualEntry = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                };
                await _accountRepo.AddAsync(header, ct);
                await _uow.SaveChangesAsync(ct);
                _logger.LogInformation("Created Owners' Drawings header account {Number}", number);
            }

            var setting = await _settingsRepo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(s => s.Key == HeaderSettingKey, ct);
            var value = header.Id.ToString();
            if (setting == null)
            {
                await _settingsRepo.AddAsync(new IntegrationSetting
                {
                    Key = HeaderSettingKey,
                    Value = value,
                    IsSecret = false,
                    Description = "Chart-of-accounts id of the Owners' Drawings header (parent of every owner's drawings account).",
                    UpdatedOn = DateTime.UtcNow
                }, ct);
                await _uow.SaveChangesAsync(ct);
            }
            else if (setting.Value != value)
            {
                setting.Value = value;
                setting.UpdatedOn = DateTime.UtcNow;
                await _uow.SaveChangesAsync(ct);
            }

            return header;
        }

        /// <summary>3300 → 3310, 3320 … 3390, then 3301 … 3399.</summary>
        private async Task<string> NextChildNumberAsync(Account header, CancellationToken ct)
        {
            var taken = (await _accountRepo.Query()
                .Where(a => a.AccountNumber.StartsWith(header.AccountNumber.Substring(0, Math.Min(2, header.AccountNumber.Length))))
                .Select(a => a.AccountNumber)
                .ToListAsync(ct)).ToHashSet();

            if (int.TryParse(header.AccountNumber, out var baseNo))
            {
                var candidates = Enumerable.Range(1, 9).Select(i => baseNo + i * 10)
                    .Concat(Enumerable.Range(1, 99).Where(i => i % 10 != 0).Select(i => baseNo + i));
                foreach (var c in candidates)
                {
                    var s = c.ToString();
                    if (!taken.Contains(s)) return s;
                }
            }

            for (var i = 1; i < 100; i++)
            {
                var s = $"{header.AccountNumber}-{i:D2}";
                if (!taken.Contains(s) && !await _accountRepo.Query().AnyAsync(a => a.AccountNumber == s, ct))
                    return s;
            }
            throw new InvalidOperationException($"No free account number under {header.AccountNumber}.");
        }

        // ============================================
        // HELPERS
        // ============================================

        private async Task EnsureTotalOwnershipAsync(decimal percent, int? excludeOwnerId, CancellationToken ct)
        {
            var others = await _ownerRepo.Query()
                .Where(o => o.IsActive && (!excludeOwnerId.HasValue || o.Id != excludeOwnerId.Value))
                .SumAsync(o => (decimal?)o.OwnershipPercent, ct) ?? 0m;
            if (others + percent > 100m)
                throw new ArgumentException($"Total ownership would be {others + percent:0.##}%. The other active owners already hold {others:0.##}%, so this owner can have at most {100m - others:0.##}%.");
        }

        private async Task<Owner> LoadActiveOwnerAsync(int ownerId, CancellationToken ct)
        {
            var owner = await _ownerRepo.Query()
                .Include(o => o.DrawingsAccount)
                .FirstOrDefaultAsync(o => o.Id == ownerId, ct)
                ?? throw new ArgumentException("Owner not found.");
            if (!owner.IsActive)
                throw new ArgumentException($"{owner.Name} is no longer an active owner.");
            return owner;
        }

        private static void ValidatePercent(decimal pct)
        {
            if (pct <= 0m || pct > 100m)
                throw new ArgumentException("Ownership % must be greater than 0 and at most 100.");
        }

        private static void ValidateDrawing(decimal amount, DateTime date)
        {
            if (amount <= 0m)
                throw new ArgumentException("Amount must be greater than zero.");
            if (date == default)
                throw new ArgumentException("Date is required.");
            if (date.Date > DateTime.UtcNow.Date.AddDays(1))
                throw new ArgumentException("A drawing cannot be dated in the future.");
        }

        private static decimal Effect(Account a, decimal debit, decimal credit)
            => a.AccountType.NormalBalance == "Debit" ? debit - credit : credit - debit;

        private static string AccountNameFor(string ownerName) => $"Drawings – {ownerName}";

        private static string DescriptionFor(string ownerName, decimal pct)
            => $"Cash taken out by {ownerName} ({pct:0.##}% owner). Contra-equity, not an expense.";

        private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

        private static string? Clean(string? s, int max = 500)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }

        private static OwnerDrawingDto ToDto(OwnerDrawing d, string ownerName, string? entryNumber) => new(
            d.Id, d.OwnerId, ownerName, d.Amount, d.DrawingDate, d.PaymentMethod, d.Comment,
            d.JournalEntryId, entryNumber, d.IsVoided, d.VoidedOn, d.VoidReason, d.CreatedBy, d.CreatedOn);
    }
}
