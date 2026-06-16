namespace Application.DTOs
{
    // ============================================
    // ACCOUNT TYPE DTOs
    // ============================================
    public record BackfillResultDto(int Total,
    int Success,
    int Failed,
    List<string> Errors
);
    public record AccountingDashboardDto(
        DateTime? From,
        DateTime? To,
        RevenueBreakdownDto Revenue,
        ExpenseSummaryDto OperatingExpenses,
        ExpenseSummaryDto CapitalExpenses,
        CogsSummaryDto Cogs,
        decimal GrossProfit,
        decimal NetIncome,
        decimal NetMarginPercent,
        // Net classification by AccountType, derived from the mapped Account on
        // each manual entry. Entries with no mapping (or whose mapped account is
        // an Expense) still flow through Operating/CapitalExpenses; this block
        // gives the dashboard a way to surface Equity (owner draws like "Omar
        // cash out") and Revenue (manual income like "Toters income") that were
        // historically lumped into Expenses.
        AccountTypeBreakdownDto? ByAccountType = null,
        // Subtotals grouped by the leading account-number prefix (1xxx Asset,
        // 2xxx Liability, 3xxx Equity, 4xxx Revenue, 5xxx Expense). Same data,
        // different lens — useful when the AccountType column is dirty.
        List<AccountRangeLineDto>? ByAccountNumberRange = null
    );

    public record AccountTypeBreakdownDto(
        decimal Asset,
        decimal Liability,
        decimal Equity,    // owner draws etc.
        decimal Revenue,   // manual revenue lines
        decimal Expense,   // real expenses
        List<AccountTypeLineDto> Lines
    );

    public record AccountTypeLineDto(
        string AccountTypeName,
        decimal Amount,
        List<ExpenseCategoryLineDto> Categories
    );

    public record AccountRangeLineDto(
        string RangeLabel,   // e.g. "5000-5999 Expense"
        string Prefix,       // e.g. "5"
        decimal Amount
    );

    // Single account reference used inside the hierarchy audit findings.
    public record HierarchyAccountRefDto(
        int Id,
        string AccountNumber,
        string AccountName,
        int AccountTypeId,
        string AccountTypeName,
        bool IsActive
    );

    // One row per type-mismatch issue: a child whose AccountType differs from
    // its parent's. Lets the admin see the actual mismatch at a glance and
    // jump straight to the bad child to remap it.
    public record HierarchyTypeMismatchDto(
        HierarchyAccountRefDto Child,
        HierarchyAccountRefDto Parent
    );

    // Header = account with at least one child. We surface ones that still
    // have AllowManualEntry=true (a misconfig — headers should never accept
    // direct postings) and ones that already DO have a non-zero direct
    // balance (someone in the past posted to a header).
    public record HierarchyHeaderIssueDto(
        HierarchyAccountRefDto Account,
        int ChildCount,
        bool AllowsManualEntry,
        decimal DirectBalance
    );

    public record AccountHierarchyAuditDto(
        int TotalAccounts,
        int TotalActive,
        List<HierarchyTypeMismatchDto> TypeMismatches,
        List<HierarchyHeaderIssueDto> HeadersAllowingManualEntry,
        List<HierarchyHeaderIssueDto> HeadersWithDirectPostings,
        List<HierarchyAccountRefDto> InactiveParentsWithActiveChildren
    );

    // Returned by GET /api/Accounting/audit-revenue-coverage. Lets the admin
    // see exactly how many paid transactions are missing journal entries and
    // what the resulting discrepancy is between the calculator (sum of
    // TransactionRecord.TotalPrice) and the chart of accounts revenue side.
    public record RevenueCoverageAuditDto(
        DateTime? From,
        DateTime? To,
        int TransactionsCount,
        decimal TransactionsTotalNet,
        decimal TransactionsTotalGross,
        int TransactionsWithJE,
        int TransactionsWithoutJE,
        List<int> OrphanTransactionIds,
        decimal RevenueAccountsCredit,      // Sum of credits on 4xxx revenue accounts in range
        decimal SalesDiscountsDebit,        // Sum of debits on 4900 in range
        decimal NetRevenueOnBooks,          // RevenueAccountsCredit - SalesDiscountsDebit
        decimal Discrepancy                 // TransactionsTotalNet - NetRevenueOnBooks
    );

    public record RevenueBreakdownDto(
        decimal Gaming,
        decimal Fnb,
        decimal Tcg,
        decimal Total,
        // Gross-of-discount breakdown. Total = Net (matches what the cashier
        // actually received and what hits Cash on Hand). Gross is the sum of
        // menu prices before discount. DiscountsGiven = Gross - Total. All
        // trailing nullable defaults keep this record backward-compatible
        // with positional callers (existing tests, etc.).
        decimal? GamingGross = null,
        decimal? FnbGross = null,
        decimal? TcgGross = null,
        decimal? TotalGross = null,
        decimal? DiscountsGiven = null
    );

    public record ExpenseSummaryDto(
        decimal Total,
        List<ExpenseCategoryLineDto> Lines
    );

    public record ExpenseCategoryLineDto(
        string Category,
        decimal Amount
    );

    public record CogsSummaryDto(
        decimal TcgCogs,  // BuyPrice x Qty for TCG items sold
        decimal Total
    );

    public record AccountTypeDto(
        int Id,
        string TypeName,
        string NormalBalance,
        int DisplayOrder,
        string? Description,
        bool IsActive
    );

    public record AccountTypeCreateDto(
        string TypeName,
        string NormalBalance, // "Debit" or "Credit"
        int DisplayOrder,
        string? Description
    );

    // ============================================
    // ACCOUNT DTOs
    // ============================================

    public record AccountDto(
        int Id,
        string AccountNumber,
        string AccountName,
        int AccountTypeId,
        string AccountTypeName,
        int? ParentAccountId,
        string? ParentAccountName,
        string? Description,
        decimal CurrentBalance,
        bool IsActive,
        bool IsSystemAccount,
        bool AllowManualEntry,
        DateTime CreatedAt
    );

    public record AccountCreateDto(
        string AccountNumber,
        string AccountName,
        int AccountTypeId,
        int? ParentAccountId,
        string? Description,
        bool AllowManualEntry = true
    );

    public record AccountUpdateDto(
        string AccountName,
        string? Description,
        bool IsActive,
        bool AllowManualEntry
    );

    public record AccountBalanceDto(
        int AccountId,
        string AccountNumber,
        string AccountName,
        string AccountTypeName,
        decimal DebitTotal,
        decimal CreditTotal,
        decimal Balance,
        string NormalBalance,
        // Rollup balance = this account's direct Balance + the sum of every
        // descendant's Balance. For header accounts (e.g. 5200) this is the
        // number people actually want when looking at "Utilities Expense" —
        // the leaf accounts indented under it.
        decimal RollupBalance = 0
    );

    public record AccountHierarchyDto(
        int Id,
        string AccountNumber,
        string AccountName,
        int AccountTypeId,
        string AccountTypeName,
        int? ParentAccountId,
        decimal CurrentBalance,
        bool IsActive,
        List<AccountHierarchyDto> Children
    );

    // ============================================
    // JOURNAL ENTRY DTOs
    // ============================================

    public record JournalEntryDto(
        int Id,
        string EntryNumber,
        DateTime EntryDate,
        string Description,
        string ReferenceType,
        int? ReferenceId,
        decimal TotalAmount,
        bool IsPosted,
        DateTime? PostedAt,
        bool IsVoided,
        DateTime? VoidedAt,
        string? VoidReason,
        DateTime CreatedAt,
        List<JournalEntryLineDto> Lines
    );

    public record JournalEntryLineDto(
        int Id,
        int JournalEntryId,
        int AccountId,
        string AccountNumber,
        string AccountName,
        decimal DebitAmount,
        decimal CreditAmount,
        string? Description,
        int LineNumber
    );

    public record JournalEntryCreateDto(
        DateTime EntryDate,
        string Description,
        string ReferenceType, // "Transaction", "Expense", "Adjustment", "Depreciation", "Opening"
        int? ReferenceId,
        List<JournalEntryLineCreateDto> Lines
    );

    public record JournalEntryLineCreateDto(
        int AccountId,
        decimal DebitAmount,
        decimal CreditAmount,
        string? Description
    );

    public record JournalEntryUpdateDto(
        DateTime EntryDate,
        string Description,
        List<JournalEntryLineCreateDto> Lines
    );

    public record PostJournalEntryDto(
        int JournalEntryId
    );

    public record VoidJournalEntryDto(
        int JournalEntryId,
        string VoidReason
    );

    // ============================================
    // REPORTING DTOs
    // ============================================

    public record TrialBalanceDto(
        DateTime AsOfDate,
        List<TrialBalanceLineDto> Lines,
        decimal TotalDebits,
        decimal TotalCredits,
        bool IsBalanced
    );

    public record TrialBalanceLineDto(
        string AccountNumber,
        string AccountName,
        string AccountTypeName,
        decimal DebitBalance,
        decimal CreditBalance
    );

    public record GeneralLedgerDto(
        int AccountId,
        string AccountNumber,
        string AccountName,
        DateTime FromDate,
        DateTime ToDate,
        decimal OpeningBalance,
        List<GeneralLedgerLineDto> Transactions,
        decimal ClosingBalance
    );

    public record GeneralLedgerLineDto(
        DateTime Date,
        string EntryNumber,
        string Description,
        decimal Debit,
        decimal Credit,
        decimal RunningBalance,
        bool IsPending = false,
        // Surfaces the JournalEntryLine.Id and parent JournalEntry.Id so the
        // Chart-of-Accounts Transactions Report UI can checkbox-select lines
        // and call the bulk re-point endpoint to move them onto a different
        // account.
        int LineId = 0,
        int JournalEntryId = 0,
        bool IsVoided = false
    );

    // Request body for POST /api/accounts/repoint-lines.
    // Reclassifies one or more JournalEntryLine rows onto NewAccountId.
    // For each line whose journal entry is posted and not voided, both old
    // and new account CurrentBalance are adjusted (mirror of what the bulk
    // backfill does). Cash-credit lines (and lines on voided entries) are
    // skipped — only debit lines on live entries can be moved.
    public record RepointLinesRequestDto(
        List<int> LineIds,
        int NewAccountId,
        string? Reason
    );

    public record RepointLinesResultDto(
        int Processed,
        int Skipped,
        int Failed,
        List<string> Errors
    );

    public record AccountSummaryDto(
        string AccountTypeName,
        decimal TotalBalance,
        List<AccountDto> Accounts
    );

    public record JournalEntrySearchDto(
        DateTime? FromDate,
        DateTime? ToDate,
        string? ReferenceType,
        int? ReferenceId,
        bool? IsPosted,
        bool? IsVoided,
        int PageNumber = 1,
        int PageSize = 50
    );

    public record AccountSearchDto(
        string? SearchTerm,
        int? AccountTypeId,
        int? ParentAccountId,
        bool? IsActive,
        int PageNumber = 1,
        int PageSize = 50
    );

    // ============================================
    // VALIDATION RESULT DTOs
    // ============================================

    public record JournalEntryValidationDto(
        bool IsValid,
        List<string> Errors,
        decimal TotalDebits,
        decimal TotalCredits,
        bool IsBalanced
    );

    public record AccountValidationDto(
        bool IsValid,
        List<string> Errors,
        bool AccountNumberUnique,
        bool AccountNameValid,
        bool ParentAccountValid
    );

    // ============================================
    // CHART OF ACCOUNTS SEEDING DTO
    // ============================================

    public record ChartOfAccountsSeedDto(
        string AccountNumber,
        string AccountName,
        string AccountTypeName,
        string? ParentAccountNumber,
        string? Description,
        bool IsSystemAccount = false,
        bool AllowManualEntry = true
    );

    /// <summary>
    /// Paged result wrapper for pagination
    /// </summary>
    public record PagedResult<T>(
        IReadOnlyList<T> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages
    );

}
