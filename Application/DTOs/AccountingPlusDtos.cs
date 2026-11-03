namespace Application.DTOs
{
    // ============================================
    // BANK ACCOUNTS & RECONCILIATION
    // ============================================

    public record BankAccountDto(
        int Id, string Name, string? BankName, string? AccountNumberMasked, string CurrencyCode,
        int GlAccountId, string? GlAccountNumber, string? GlAccountName, bool IsActive, int? BranchId, DateTime CreatedOn);

    public record BankAccountUpsertDto(
        string Name, string? BankName, string? AccountNumberMasked, string? CurrencyCode, int GlAccountId, bool IsActive = true);

    public record BankStatementLineCreateDto(DateTime Date, string? Description, string? Reference, decimal Amount);

    public record BankStatementCreateDto(
        int BankAccountId, DateTime PeriodFrom, DateTime PeriodTo, decimal OpeningBalance, decimal ClosingBalance,
        List<BankStatementLineCreateDto>? Lines);

    public record BankStatementLineDto(
        int Id, int StatementId, DateTime Date, string? Description, string? Reference, decimal Amount,
        int? MatchedJournalEntryLineId, string MatchStatus, DateTime? MatchedOn, string? MatchedBy, string? Note);

    public record BankStatementDto(
        int Id, int BankAccountId, string? BankAccountName, DateTime PeriodFrom, DateTime PeriodTo,
        decimal OpeningBalance, decimal ClosingBalance, DateTime ImportedOn, string? ImportedBy, string Status,
        DateTime? ReconciledOn, int LineCount, int UnmatchedCount);

    public record BankStatementDetailDto(BankStatementDto Statement, List<BankStatementLineDto> Lines);

    /// <summary>A posted ledger line on the bank GL account that could match a statement line.</summary>
    public record BankMatchSuggestionDto(
        int JournalEntryLineId, int JournalEntryId, string EntryNumber, DateTime EntryDate, string Description,
        decimal DebitAmount, decimal CreditAmount, decimal SignedAmount, int DayDifference);

    public record BankMatchRequestDto(int JournalEntryLineId);
    public record BankExcludeRequestDto(string? Note);

    public record BankReconciliationSummaryDto(
        int StatementId, DateTime PeriodFrom, DateTime PeriodTo, decimal OpeningBalance, decimal ClosingBalance,
        decimal LedgerBalanceAsOfPeriodTo, decimal Difference,
        int TotalLines, int MatchedCount, int UnmatchedCount, int ExcludedCount,
        decimal UnmatchedTotal, decimal MatchedTotal, decimal StatementNetMovement,
        int UnmatchedLedgerLinesInPeriod, decimal UnmatchedLedgerTotalInPeriod,
        bool CanReconcile, string Status);

    public record CsvImportResultDto(int StatementId, int Imported, int Skipped, List<string> Errors);

    // ============================================
    // SUPPLIER BILLS / AP
    // ============================================

    public record SupplierBillPaymentDto(
        int Id, int BillId, DateTime PaidOn, decimal Amount, string? Method, string? Reference,
        int CashGlAccountId, int? JournalEntryId, string? CreatedBy, DateTime CreatedOn);

    public record SupplierBillDto(
        int Id, int SupplierId, string? SupplierName, string BillNumber, DateTime BillDate, DateTime DueDate,
        decimal Subtotal, decimal TaxAmount, decimal Total, decimal PaidAmount, decimal Outstanding, string Status,
        int? PurchaseId, int ExpenseGlAccountId, int PayableGlAccountId, int? JournalEntryId, string? Notes,
        DateTime CreatedOn, string? CreatedBy, int? BranchId, List<SupplierBillPaymentDto> Payments);

    public record SupplierBillCreateDto(
        int SupplierId, string BillNumber, DateTime BillDate, DateTime DueDate, decimal Subtotal, decimal TaxAmount,
        int ExpenseGlAccountId, int? PayableGlAccountId, int? PurchaseId, string? Notes,
        // "Draft" keeps it unposted; "Open" posts the journal entry immediately.
        string Status = "Open");

    public record SupplierBillUpdateDto(
        string BillNumber, DateTime BillDate, DateTime DueDate, decimal Subtotal, decimal TaxAmount,
        int ExpenseGlAccountId, int? PayableGlAccountId, int? PurchaseId, string? Notes);

    public record SupplierBillPaymentCreateDto(DateTime PaidOn, decimal Amount, string? Method, string? Reference, int CashGlAccountId);

    public record SupplierBillFilterDto(
        int? SupplierId = null, string? Status = null, DateTime? From = null, DateTime? To = null,
        string? Search = null, int Page = 1, int PageSize = 20);

    public record VoidRequestDto(string? Reason);

    public record AgingBucketsDto(decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);

    public record ApAgingRowDto(int SupplierId, string SupplierName, int OpenBills, AgingBucketsDto Buckets);

    public record ApAgingReportDto(DateTime AsOf, List<ApAgingRowDto> Rows, AgingBucketsDto Totals);

    public record ArAgingRowDto(int? UserId, string CustomerName, int OpenInvoices, AgingBucketsDto Buckets);

    public record ArAgingReportDto(DateTime AsOf, List<ArAgingRowDto> Rows, AgingBucketsDto Totals);

    public record DueSoonBillDto(int Id, int SupplierId, string? SupplierName, string BillNumber, DateTime DueDate, int DaysUntilDue, decimal Outstanding, string Status);

    // ============================================
    // TAX
    // ============================================

    public record TaxRateDto(int Id, string Name, decimal RatePercent, bool IsDefault, bool IsActive, string AppliesTo);
    public record TaxRateUpsertDto(string Name, decimal RatePercent, bool IsDefault, bool IsActive, string AppliesTo);

    public record VatReportRowDto(
        int Year, int Month, decimal SalesGross, decimal SalesNet, decimal OutputVat,
        decimal PurchasesNet, decimal InputVat, decimal NetPayable);

    public record VatReportDto(
        DateTime From, DateTime To, decimal SalesRatePercent, bool Inclusive, List<VatReportRowDto> Rows,
        decimal TotalOutputVat, decimal TotalInputVat, decimal TotalNetPayable);

    // ============================================
    // FIXED ASSETS
    // ============================================

    public record FixedAssetDto(
        int Id, string Name, string? Category, DateTime AcquiredOn, decimal Cost, decimal SalvageValue,
        int UsefulLifeMonths, string Method, int AssetGlAccountId, int AccumulatedDepGlAccountId, int DepExpenseGlAccountId,
        string Status, DateTime? DisposedOn, decimal? DisposalProceeds, string? Notes, int? BranchId,
        decimal AccumulatedDepreciation, decimal NetBookValue, int DepreciationEntriesCount);

    public record FixedAssetUpsertDto(
        string Name, string? Category, DateTime AcquiredOn, decimal Cost, decimal SalvageValue, int UsefulLifeMonths,
        string Method, int AssetGlAccountId, int AccumulatedDepGlAccountId, int DepExpenseGlAccountId, string? Notes);

    public record DepreciationScheduleRowDto(int Year, int Month, decimal Amount, decimal AccumulatedAfter, decimal NetBookValueAfter, bool Posted);

    public record DepreciationRunResultDto(
        int Year, int Month, int AssetsProcessed, int Posted, int Skipped, decimal TotalAmount, List<string> Messages);

    public record DepreciationEntryDto(int Id, int AssetId, int PeriodYear, int PeriodMonth, decimal Amount, int? JournalEntryId, DateTime PostedOn, string? PostedBy);

    public record AssetDisposeDto(DateTime DisposedOn, decimal Proceeds, int CashGlAccountId, int GainLossGlAccountId, string? Notes);

    public record AssetRegisterSummaryDto(
        int ActiveAssets, int DisposedAssets, decimal TotalCost, decimal TotalAccumulatedDepreciation, decimal TotalNetBookValue,
        List<FixedAssetDto> Assets);

    // ============================================
    // PERIODS
    // ============================================

    public record AccountingPeriodDto(int Year, int Month, bool IsLocked, DateTime? LockedOn, string? LockedBy, string? Note);
    public record PeriodLockRequestDto(int Year, int Month, string? Note);
}
