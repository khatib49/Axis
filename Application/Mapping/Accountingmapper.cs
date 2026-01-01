using Application.DTOs;
using Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Application.Mapping
{
    /// <summary>
    /// Accounting system mapper using Riok.Mappilicy
    /// Handles mapping between entities and DTOs for Chart of Accounts and Journal Entries
    /// </summary>
    [Mapper]
    public partial class AccountingMapper
    {
        // ============================================
        // ACCOUNT TYPE MAPPINGS
        // ============================================

        public AccountTypeDto ToDto(AccountType entity) =>
            new AccountTypeDto(
                entity.Id,
                entity.TypeName,
                entity.NormalBalance,
                entity.DisplayOrder,
                entity.Description,
                entity.IsActive
            );

        public partial AccountType ToEntity(AccountTypeCreateDto dto);

        // ============================================
        // ACCOUNT MAPPINGS
        // ============================================

        public AccountDto ToDto(Account entity) =>
            new AccountDto(
                entity.Id,
                entity.AccountNumber,
                entity.AccountName,
                entity.AccountTypeId,
                entity.AccountType?.TypeName ?? string.Empty,
                entity.ParentAccountId,
                entity.ParentAccount?.AccountName,
                entity.Description,
                entity.CurrentBalance,
                entity.IsActive,
                entity.IsSystemAccount,
                entity.AllowManualEntry,
                entity.CreatedAt
            );

        public partial Account ToEntity(AccountCreateDto dto);

        /// <summary>
        /// Map account with balance information
        /// </summary>
        public AccountBalanceDto ToBalanceDto(Account entity, decimal debitTotal, decimal creditTotal)
        {
            var balance = entity.AccountType.NormalBalance == "Debit"
                ? debitTotal - creditTotal
                : creditTotal - debitTotal;

            return new AccountBalanceDto(
                entity.Id,
                entity.AccountNumber,
                entity.AccountName,
                entity.AccountType.TypeName,
                debitTotal,
                creditTotal,
                balance,
                entity.AccountType.NormalBalance
            );
        }

        /// <summary>
        /// Map account hierarchy recursively
        /// </summary>
        public AccountHierarchyDto ToHierarchyDto(Account entity)
        {
            var children = entity.ChildAccounts
                .Where(c => c.IsActive)
                .Select(c => ToHierarchyDto(c))
                .OrderBy(c => c.AccountNumber)
                .ToList();

            return new AccountHierarchyDto(
                entity.Id,
                entity.AccountNumber,
                entity.AccountName,
                entity.AccountTypeId,
                entity.AccountType?.TypeName ?? string.Empty,
                entity.ParentAccountId,
                entity.CurrentBalance,
                entity.IsActive,
                children
            );
        }

        // ============================================
        // JOURNAL ENTRY MAPPINGS
        // ============================================

        public JournalEntryDto ToDto(JournalEntry entity) =>
            new JournalEntryDto(
                entity.Id,
                entity.EntryNumber,
                entity.EntryDate,
                entity.Description,
                entity.ReferenceType,
                entity.ReferenceId,
                entity.TotalAmount,
                entity.IsPosted,
                entity.PostedAt,
                entity.IsVoided,
                entity.VoidedAt,
                entity.VoidReason,
                entity.CreatedAt,
                entity.Lines.Select(l => ToDto(l)).OrderBy(l => l.LineNumber).ToList()
            );

        public JournalEntryLineDto ToDto(JournalEntryLine entity) =>
            new JournalEntryLineDto(
                entity.Id,
                entity.JournalEntryId,
                entity.AccountId,
                entity.Account?.AccountNumber ?? string.Empty,
                entity.Account?.AccountName ?? string.Empty,
                entity.DebitAmount,
                entity.CreditAmount,
                entity.Description,
                entity.LineNumber
            );

        public partial JournalEntry ToEntity(JournalEntryCreateDto dto);

        // ============================================
        // REPORTING MAPPINGS
        // ============================================

        public TrialBalanceLineDto ToTrialBalanceLineDto(
            string accountNumber,
            string accountName,
            string accountTypeName,
            decimal debitBalance,
            decimal creditBalance) =>
            new TrialBalanceLineDto(
                accountNumber,
                accountName,
                accountTypeName,
                debitBalance,
                creditBalance
            );

        public GeneralLedgerLineDto ToGeneralLedgerLineDto(
            DateTime date,
            string entryNumber,
            string description,
            decimal debit,
            decimal credit,
            decimal runningBalance) =>
            new GeneralLedgerLineDto(
                date,
                entryNumber,
                description,
                debit,
                credit,
                runningBalance
            );
    }
}