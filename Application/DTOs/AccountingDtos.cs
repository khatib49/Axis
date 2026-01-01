using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    // ============================================
    // ACCOUNT TYPE DTOs
    // ============================================

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
        string NormalBalance
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
        decimal RunningBalance
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
