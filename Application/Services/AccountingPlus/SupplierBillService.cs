using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.AccountingPlus
{
    public class SupplierBillService : ISupplierBillService
    {
        public const string DefaultPayableAccountNumber = "2000"; // ChartOfAccountsSeeder: Accounts Payable
        private static readonly string[] OpenStatuses = { "Open", "PartiallyPaid" };

        private readonly ApplicationDbContext _db;
        private readonly IJournalService _journal;
        private readonly IAccountingPeriodService _periods;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<SupplierBillService> _logger;

        public SupplierBillService(
            ApplicationDbContext db,
            IJournalService journal,
            IAccountingPeriodService periods,
            ICurrentUserAccessor user,
            ILogger<SupplierBillService> logger)
        {
            _db = db;
            _journal = journal;
            _periods = periods;
            _user = user;
            _logger = logger;
        }

        // ============================================
        // CRUD
        // ============================================

        public async Task<PaginatedResponse<SupplierBillDto>> ListAsync(SupplierBillFilterDto filter, CancellationToken ct = default)
        {
            var q = _db.Set<SupplierBill>().AsNoTracking().Include(b => b.Payments).AsQueryable();
            if (filter.SupplierId.HasValue) q = q.Where(b => b.SupplierId == filter.SupplierId.Value);
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                var st = filter.Status.Trim();
                q = st.Equals("Unpaid", StringComparison.OrdinalIgnoreCase)
                    ? q.Where(b => b.Status == "Open" || b.Status == "PartiallyPaid")
                    : q.Where(b => b.Status == st);
            }
            if (filter.From.HasValue) q = q.Where(b => b.BillDate >= AcctDates.StartOfDay(filter.From.Value));
            if (filter.To.HasValue) q = q.Where(b => b.BillDate <= AcctDates.EndOfDay(filter.To.Value));

            var supplierNames = await _db.Set<Supplier>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var term = filter.Search.Trim().ToLowerInvariant();
                var supplierIds = supplierNames.Where(kv => kv.Value.ToLowerInvariant().Contains(term)).Select(kv => kv.Key).ToList();
                q = q.Where(b => b.BillNumber.ToLower().Contains(term) || (b.Notes != null && b.Notes.ToLower().Contains(term)) || supplierIds.Contains(b.SupplierId));
            }

            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(b => b.BillDate).ThenByDescending(b => b.Id)
                .Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PaginatedResponse<SupplierBillDto>(total, rows.Select(b => ToDto(b, supplierNames.GetValueOrDefault(b.SupplierId))).ToList(), page, size);
        }

        public async Task<BaseResponse<SupplierBillDto>> GetAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.Set<SupplierBill>().AsNoTracking().Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return new BaseResponse<SupplierBillDto>(false, "Bill not found", "");
            var name = await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == b.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
            return new BaseResponse<SupplierBillDto>(true, null, "", ToDto(b, name));
        }

        public async Task<BaseResponse<SupplierBillDto>> CreateAsync(SupplierBillCreateDto dto, CancellationToken ct = default)
        {
            var status = string.Equals(dto.Status, "Draft", StringComparison.OrdinalIgnoreCase) ? "Draft" : "Open";
            var validation = await ValidateAsync(dto.SupplierId, dto.BillNumber, dto.BillDate, dto.DueDate, dto.Subtotal, dto.TaxAmount,
                dto.ExpenseGlAccountId, dto.PayableGlAccountId, dto.PurchaseId, null, ct);
            if (validation.Error is not null) return new BaseResponse<SupplierBillDto>(false, validation.Error, "");

            if (status == "Open")
            {
                var open = await _periods.EnsureOpenAsync(dto.BillDate, ct);
                if (!open.Success) return new BaseResponse<SupplierBillDto>(false, open.Error, "");
            }

            var bill = new SupplierBill
            {
                SupplierId = dto.SupplierId,
                BillNumber = dto.BillNumber.Trim(),
                BillDate = AcctDates.StartOfDay(dto.BillDate),
                DueDate = AcctDates.StartOfDay(dto.DueDate),
                Subtotal = Math.Round(dto.Subtotal, 2),
                TaxAmount = Math.Round(dto.TaxAmount, 2),
                Total = Math.Round(dto.Subtotal + dto.TaxAmount, 2),
                PaidAmount = 0,
                Status = "Draft",
                PurchaseId = dto.PurchaseId,
                ExpenseGlAccountId = dto.ExpenseGlAccountId,
                PayableGlAccountId = validation.PayableAccountId,
                Notes = dto.Notes?.Trim(),
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
                BranchId = _user.BranchId,
            };
            _db.Add(bill);
            await _db.SaveChangesAsync(ct);

            if (status == "Open")
            {
                var posted = await PostBillAsync(bill, ct);
                if (!posted.Success) return new BaseResponse<SupplierBillDto>(false, posted.Error, "", (await GetAsync(bill.Id, ct)).Data);
            }
            return await GetAsync(bill.Id, ct);
        }

        public async Task<BaseResponse<SupplierBillDto>> UpdateDraftAsync(int id, SupplierBillUpdateDto dto, CancellationToken ct = default)
        {
            var bill = await _db.Set<SupplierBill>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null) return new BaseResponse<SupplierBillDto>(false, "Bill not found", "");
            if (bill.Status != "Draft") return new BaseResponse<SupplierBillDto>(false, "Only Draft bills can be edited", "");

            var validation = await ValidateAsync(bill.SupplierId, dto.BillNumber, dto.BillDate, dto.DueDate, dto.Subtotal, dto.TaxAmount,
                dto.ExpenseGlAccountId, dto.PayableGlAccountId, dto.PurchaseId, bill.Id, ct);
            if (validation.Error is not null) return new BaseResponse<SupplierBillDto>(false, validation.Error, "");

            bill.BillNumber = dto.BillNumber.Trim();
            bill.BillDate = AcctDates.StartOfDay(dto.BillDate);
            bill.DueDate = AcctDates.StartOfDay(dto.DueDate);
            bill.Subtotal = Math.Round(dto.Subtotal, 2);
            bill.TaxAmount = Math.Round(dto.TaxAmount, 2);
            bill.Total = Math.Round(dto.Subtotal + dto.TaxAmount, 2);
            bill.PurchaseId = dto.PurchaseId;
            bill.ExpenseGlAccountId = dto.ExpenseGlAccountId;
            bill.PayableGlAccountId = validation.PayableAccountId;
            bill.Notes = dto.Notes?.Trim();
            await _db.SaveChangesAsync(ct);
            return await GetAsync(bill.Id, ct);
        }

        public async Task<BaseResponse<SupplierBillDto>> OpenAsync(int id, CancellationToken ct = default)
        {
            var bill = await _db.Set<SupplierBill>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null) return new BaseResponse<SupplierBillDto>(false, "Bill not found", "");
            if (bill.Status != "Draft") return new BaseResponse<SupplierBillDto>(false, "Bill is not a Draft", "");
            var open = await _periods.EnsureOpenAsync(bill.BillDate, ct);
            if (!open.Success) return new BaseResponse<SupplierBillDto>(false, open.Error, "");
            var posted = await PostBillAsync(bill, ct);
            if (!posted.Success) return new BaseResponse<SupplierBillDto>(false, posted.Error, "");
            return await GetAsync(bill.Id, ct);
        }

        public async Task<BaseResponse<SupplierBillDto>> AddPaymentAsync(int id, SupplierBillPaymentCreateDto dto, CancellationToken ct = default)
        {
            var bill = await _db.Set<SupplierBill>().Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null) return new BaseResponse<SupplierBillDto>(false, "Bill not found", "");
            if (!OpenStatuses.Contains(bill.Status)) return new BaseResponse<SupplierBillDto>(false, $"Cannot pay a bill in status {bill.Status}", "");
            if (dto.Amount <= 0) return new BaseResponse<SupplierBillDto>(false, "Amount must be positive", "");
            var outstanding = bill.Total - bill.PaidAmount;
            if (dto.Amount > outstanding + 0.005m) return new BaseResponse<SupplierBillDto>(false, $"Amount exceeds outstanding balance ({outstanding:0.00})", "");

            var cash = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.CashGlAccountId && a.IsActive, ct);
            if (cash is null) return new BaseResponse<SupplierBillDto>(false, "Cash/bank GL account not found or inactive", "");

            var open = await _periods.EnsureOpenAsync(dto.PaidOn, ct);
            if (!open.Success) return new BaseResponse<SupplierBillDto>(false, open.Error, "");

            var amount = Math.Round(dto.Amount, 2);
            var paidOn = AcctDates.Utc(dto.PaidOn);
            var je = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(
                paidOn,
                $"Payment to supplier for bill {bill.BillNumber}",
                "SupplierBillPayment",
                bill.Id,
                new List<JournalEntryLineCreateDto>
                {
                    new(bill.PayableGlAccountId, amount, 0, $"Bill {bill.BillNumber} payment"),
                    new(cash.Id, 0, amount, $"Bill {bill.BillNumber} payment ({dto.Method})"),
                }), _user.UserId, ct);
            if (!je.Success || je.Data is null) return new BaseResponse<SupplierBillDto>(false, $"Journal entry failed: {je.Error}", "");
            var posted = await _journal.PostJournalEntryAsync(je.Data.Id, _user.UserId, ct);
            if (!posted.Success) return new BaseResponse<SupplierBillDto>(false, $"Journal posting failed: {posted.Error}", "");

            bill.Payments.Add(new SupplierBillPayment
            {
                PaidOn = paidOn,
                Amount = amount,
                Method = dto.Method?.Trim(),
                Reference = dto.Reference?.Trim(),
                CashGlAccountId = cash.Id,
                JournalEntryId = je.Data.Id,
                CreatedBy = _user.UserName,
                CreatedOn = DateTime.UtcNow,
            });
            bill.PaidAmount = Math.Round(bill.PaidAmount + amount, 2);
            bill.Status = bill.PaidAmount >= bill.Total - 0.005m ? "Paid" : "PartiallyPaid";
            await _db.SaveChangesAsync(ct);
            return await GetAsync(bill.Id, ct);
        }

        public async Task<BaseResponse<SupplierBillDto>> VoidAsync(int id, string? reason, CancellationToken ct = default)
        {
            var bill = await _db.Set<SupplierBill>().Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null) return new BaseResponse<SupplierBillDto>(false, "Bill not found", "");
            if (bill.Status == "Void") return new BaseResponse<SupplierBillDto>(false, "Bill is already void", "");

            var open = await _periods.EnsureOpenAsync(DateTime.UtcNow, ct);
            if (!open.Success) return new BaseResponse<SupplierBillDto>(false, open.Error, "");

            var why = string.IsNullOrWhiteSpace(reason) ? $"Supplier bill {bill.BillNumber} voided" : reason.Trim();
            foreach (var p in bill.Payments.Where(p => p.JournalEntryId.HasValue))
            {
                var v = await _journal.VoidJournalEntryAsync(p.JournalEntryId!.Value, why, _user.UserId, ct);
                if (!v.Success && !string.Equals(v.Error, "Journal entry already voided", StringComparison.OrdinalIgnoreCase))
                    return new BaseResponse<SupplierBillDto>(false, $"Could not void payment journal: {v.Error}", "");
            }
            if (bill.JournalEntryId.HasValue)
            {
                var v = await _journal.VoidJournalEntryAsync(bill.JournalEntryId.Value, why, _user.UserId, ct);
                if (!v.Success && !string.Equals(v.Error, "Journal entry already voided", StringComparison.OrdinalIgnoreCase))
                    return new BaseResponse<SupplierBillDto>(false, $"Could not void bill journal: {v.Error}", "");
            }
            bill.Status = "Void";
            bill.Notes = string.IsNullOrWhiteSpace(bill.Notes) ? $"VOID: {why}" : $"{bill.Notes}\nVOID: {why}";
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Supplier bill {Id} voided by {User}", bill.Id, _user.UserName);
            return await GetAsync(bill.Id, ct);
        }

        public async Task<BaseResponse> DeleteDraftAsync(int id, CancellationToken ct = default)
        {
            var bill = await _db.Set<SupplierBill>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null) return new BaseResponse(false, "Bill not found");
            if (bill.Status != "Draft") return new BaseResponse(false, "Only Draft bills can be deleted; void it instead");
            _db.Remove(bill);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse(true, null);
        }

        // ============================================
        // REPORTS
        // ============================================

        public async Task<ApAgingReportDto> GetApAgingAsync(DateTime? asOf, CancellationToken ct = default)
        {
            var at = AcctDates.Utc(asOf ?? DateTime.UtcNow).Date;
            var bills = await _db.Set<SupplierBill>().AsNoTracking()
                .Where(b => (b.Status == "Open" || b.Status == "PartiallyPaid") && b.BillDate <= AcctDates.EndOfDay(at))
                .ToListAsync(ct);
            var names = await _db.Set<Supplier>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            var rows = new List<ApAgingRowDto>();
            var totals = new AgingBuckets.Accumulator();
            foreach (var g in bills.GroupBy(b => b.SupplierId))
            {
                var acc = new AgingBuckets.Accumulator();
                foreach (var b in g) acc.Add(b.DueDate, at, b.Total - b.PaidAmount);
                totals.Add(acc);
                rows.Add(new ApAgingRowDto(g.Key, names.GetValueOrDefault(g.Key) ?? $"Supplier #{g.Key}", acc.Count, acc.ToDto()));
            }
            return new ApAgingReportDto(at, rows.OrderByDescending(r => r.Buckets.Total).ToList(), totals.ToDto());
        }

        public async Task<List<DueSoonBillDto>> GetDueSoonAsync(int days, CancellationToken ct = default)
        {
            var today = DateTime.UtcNow.Date;
            var until = today.AddDays(Math.Max(0, days)).AddDays(1).AddTicks(-1);
            var bills = await _db.Set<SupplierBill>().AsNoTracking()
                .Where(b => (b.Status == "Open" || b.Status == "PartiallyPaid") && b.DueDate <= until)
                .OrderBy(b => b.DueDate).ToListAsync(ct);
            var names = await _db.Set<Supplier>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            return bills.Select(b => new DueSoonBillDto(b.Id, b.SupplierId, names.GetValueOrDefault(b.SupplierId), b.BillNumber, b.DueDate,
                (b.DueDate.Date - today).Days, Math.Round(b.Total - b.PaidAmount, 2), b.Status)).ToList();
        }

        // ============================================
        // HELPERS
        // ============================================

        private sealed record Validation(string? Error, int PayableAccountId);

        private async Task<Validation> ValidateAsync(int supplierId, string billNumber, DateTime billDate, DateTime dueDate, decimal subtotal, decimal tax,
            int expenseAccountId, int? payableAccountId, int? purchaseId, int? existingBillId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(billNumber)) return new Validation("Bill number is required", 0);
            if (subtotal < 0 || tax < 0) return new Validation("Amounts cannot be negative", 0);
            if (subtotal + tax <= 0) return new Validation("Bill total must be positive", 0);
            if (dueDate.Date < billDate.Date) return new Validation("Due date cannot be before bill date", 0);

            var supplierExists = await _db.Set<Supplier>().AsNoTracking().AnyAsync(s => s.Id == supplierId, ct);
            if (!supplierExists) return new Validation("Supplier not found", 0);

            var number = billNumber.Trim();
            var dup = await _db.Set<SupplierBill>().AsNoTracking()
                .AnyAsync(b => b.SupplierId == supplierId && b.BillNumber == number && b.Id != (existingBillId ?? 0), ct);
            if (dup) return new Validation("A bill with this number already exists for this supplier", 0);

            var expense = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == expenseAccountId, ct);
            if (expense is null || !expense.IsActive) return new Validation("Expense GL account not found or inactive", 0);

            if (purchaseId.HasValue && !await _db.Set<Purchase>().AsNoTracking().AnyAsync(p => p.Id == purchaseId.Value, ct))
                return new Validation("Linked purchase not found", 0);

            Account? payable = null;
            if (payableAccountId.HasValue)
                payable = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == payableAccountId.Value && a.IsActive, ct);
            payable ??= await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == DefaultPayableAccountNumber && a.IsActive, ct);
            if (payable is null) return new Validation("Payable GL account is required (no default Accounts Payable account found)", 0);

            return new Validation(null, payable.Id);
        }

        /// <summary>DR expense (+ DR VAT input when configured) / CR payable, then posts and flips the bill to Open.</summary>
        private async Task<BaseResponse> PostBillAsync(SupplierBill bill, CancellationToken ct)
        {
            var lines = new List<JournalEntryLineCreateDto>();
            var vatInput = bill.TaxAmount > 0 ? await ResolveVatInputAccountAsync(ct) : null;
            if (vatInput is not null)
            {
                if (bill.Subtotal > 0) lines.Add(new(bill.ExpenseGlAccountId, bill.Subtotal, 0, $"Bill {bill.BillNumber}"));
                lines.Add(new(vatInput.Id, bill.TaxAmount, 0, $"Bill {bill.BillNumber} VAT"));
            }
            else
            {
                lines.Add(new(bill.ExpenseGlAccountId, bill.Total, 0, $"Bill {bill.BillNumber}"));
            }
            lines.Add(new(bill.PayableGlAccountId, 0, bill.Total, $"Bill {bill.BillNumber} payable"));

            var je = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(
                bill.BillDate, $"Supplier bill {bill.BillNumber}", "SupplierBill", bill.Id, lines), _user.UserId, ct);
            if (!je.Success || je.Data is null) return new BaseResponse(false, $"Journal entry failed: {je.Error}");
            var posted = await _journal.PostJournalEntryAsync(je.Data.Id, _user.UserId, ct);
            if (!posted.Success) return new BaseResponse(false, $"Journal posting failed: {posted.Error}");

            bill.JournalEntryId = je.Data.Id;
            bill.Status = "Open";
            await _db.SaveChangesAsync(ct);
            return new BaseResponse(true, null);
        }

        /// <summary>
        /// A "VAT input" account is only used when a purchase tax rate is configured and an
        /// active account whose name contains both "vat" and "input" exists; otherwise tax
        /// is booked into the expense account.
        /// </summary>
        private async Task<Account?> ResolveVatInputAccountAsync(CancellationToken ct)
        {
            var hasPurchaseRate = await _db.Set<TaxRate>().AsNoTracking()
                .AnyAsync(t => t.IsActive && (t.AppliesTo == "Purchases" || t.AppliesTo == "Both"), ct);
            if (!hasPurchaseRate) return null;
            var accounts = await _db.Set<Account>().AsNoTracking().Where(a => a.IsActive).ToListAsync(ct);
            return accounts.FirstOrDefault(a =>
                a.AccountName.Contains("vat", StringComparison.OrdinalIgnoreCase) &&
                a.AccountName.Contains("input", StringComparison.OrdinalIgnoreCase));
        }

        private static SupplierBillDto ToDto(SupplierBill b, string? supplierName) => new(
            b.Id, b.SupplierId, supplierName, b.BillNumber, b.BillDate, b.DueDate, b.Subtotal, b.TaxAmount, b.Total, b.PaidAmount,
            Math.Round(b.Total - b.PaidAmount, 2), b.Status, b.PurchaseId, b.ExpenseGlAccountId, b.PayableGlAccountId, b.JournalEntryId,
            b.Notes, b.CreatedOn, b.CreatedBy, b.BranchId,
            b.Payments.OrderBy(p => p.PaidOn).Select(p => new SupplierBillPaymentDto(
                p.Id, p.BillId, p.PaidOn, p.Amount, p.Method, p.Reference, p.CashGlAccountId, p.JournalEntryId, p.CreatedBy, p.CreatedOn)).ToList());
    }
}
