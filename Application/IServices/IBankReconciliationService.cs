using Application.DTOs;

namespace Application.IServices
{
    public interface IBankReconciliationService
    {
        // Bank accounts
        Task<List<BankAccountDto>> ListBankAccountsAsync(bool includeInactive, CancellationToken ct = default);
        Task<BaseResponse<BankAccountDto>> GetBankAccountAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<BankAccountDto>> CreateBankAccountAsync(BankAccountUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<BankAccountDto>> UpdateBankAccountAsync(int id, BankAccountUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse> DeleteBankAccountAsync(int id, CancellationToken ct = default);

        // Statements
        Task<List<BankStatementDto>> ListStatementsAsync(int? bankAccountId, CancellationToken ct = default);
        Task<BaseResponse<BankStatementDto>> CreateStatementAsync(BankStatementCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<CsvImportResultDto>> ImportCsvAsync(BankStatementCreateDto header, string csvContent, CancellationToken ct = default);
        Task<BaseResponse<BankStatementDetailDto>> GetStatementAsync(int statementId, string? matchStatus, CancellationToken ct = default);
        Task<BaseResponse> DeleteStatementAsync(int statementId, CancellationToken ct = default);

        // Matching
        Task<BaseResponse<List<BankMatchSuggestionDto>>> GetSuggestionsAsync(int statementId, int lineId, CancellationToken ct = default);
        Task<BaseResponse<BankStatementLineDto>> MatchLineAsync(int statementId, int lineId, int journalEntryLineId, CancellationToken ct = default);
        Task<BaseResponse<BankStatementLineDto>> UnmatchLineAsync(int statementId, int lineId, CancellationToken ct = default);
        Task<BaseResponse<BankStatementLineDto>> ExcludeLineAsync(int statementId, int lineId, string? note, CancellationToken ct = default);

        Task<BaseResponse<BankReconciliationSummaryDto>> GetSummaryAsync(int statementId, CancellationToken ct = default);
        Task<BaseResponse<BankReconciliationSummaryDto>> ReconcileAsync(int statementId, CancellationToken ct = default);
    }
}
