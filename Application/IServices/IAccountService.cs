using Application.DTOs;
using Application.Services;

namespace Application.IServices
{
    public interface IAccountService
    {

        Task<List<ExpenseAccountDto>> GetExpenseAccountsAsync(CancellationToken ct);
        Task<BaseResponse<IReadOnlyList<AccountTypeDto>>> GetAllAccountTypesAsync(CancellationToken ct = default);

        Task<BaseResponse<AccountTypeDto>> GetAccountTypeByIdAsync(int id, CancellationToken ct = default);

        // ============================================
        // ACCOUNT OPERATIONS
        // ============================================

        Task<BaseResponse<AccountDto>> CreateAccountAsync(
            AccountCreateDto dto,
            int? createdBy,
            CancellationToken ct = default);

        Task<BaseResponse<AccountDto>> UpdateAccountAsync(
            int id,
            AccountUpdateDto dto,
            int? modifiedBy,
            CancellationToken ct = default);

        Task<BaseResponse> DeactivateAccountAsync(int id, int? modifiedBy, CancellationToken ct = default);

        Task<BaseResponse<AccountDto>> GetAccountByIdAsync(int id, CancellationToken ct = default);

        Task<BaseResponse<AccountDto>> GetAccountByNumberAsync(string accountNumber, CancellationToken ct = default);

        Task<BaseResponse<IReadOnlyList<AccountDto>>> GetAllAccountsAsync(
            int? accountTypeId = null,
            bool? isActive = null,
            CancellationToken ct = default);

        Task<BaseResponse<PagedResult<AccountDto>>> SearchAccountsAsync(
            AccountSearchDto searchDto,
            CancellationToken ct = default);

        Task<BaseResponse<IReadOnlyList<AccountHierarchyDto>>> GetAccountHierarchyAsync(
            int? accountTypeId = null,
            CancellationToken ct = default);

        // ============================================
        // BALANCE & REPORTING
        // ============================================

        Task<BaseResponse<AccountBalanceDto>> GetAccountBalanceAsync(int accountId, CancellationToken ct = default);

        Task<BaseResponse<IReadOnlyList<AccountBalanceDto>>> GetAllAccountBalancesAsync(CancellationToken ct = default);

        Task<BaseResponse<IReadOnlyList<AccountSummaryDto>>> GetAccountSummaryAsync(CancellationToken ct = default);

        Task<BaseResponse<TrialBalanceDto>> GetTrialBalanceAsync(DateTime? asOfDate = null, CancellationToken ct = default);

        Task<BaseResponse<GeneralLedgerDto>> GetGeneralLedgerAsync(
            int accountId,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken ct = default);

        // ============================================
        // VALIDATION
        // ============================================

        Task<AccountValidationDto> ValidateAccountAsync(
            string accountNumber,
            string accountName,
            int accountTypeId,
            int? parentAccountId,
            int? existingAccountId = null,
            CancellationToken ct = default);
    }
}
