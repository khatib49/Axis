using Application.DTOs;

namespace Application.IServices
{
    public interface IJournalService
    {
        Task<BaseResponse<JournalEntryDto>> CreateJournalEntryAsync(
            JournalEntryCreateDto dto,
            int? createdBy,
            CancellationToken ct = default);

        Task<BaseResponse<JournalEntryDto>> UpdateJournalEntryAsync(
            int id,
            JournalEntryUpdateDto dto,
            int? modifiedBy,
            CancellationToken ct = default);

        Task<BaseResponse<JournalEntryDto>> PostJournalEntryAsync(
            int id,
            int? postedBy,
            CancellationToken ct = default);

        Task<BaseResponse<JournalEntryDto>> VoidJournalEntryAsync(
            int id,
            string voidReason,
            int? voidedBy,
            CancellationToken ct = default);

        Task<BaseResponse> DeleteJournalEntryAsync(int id, CancellationToken ct = default);

        // ============================================
        // QUERY OPERATIONS
        // ============================================

        Task<BaseResponse<JournalEntryDto>> GetJournalEntryByIdAsync(int id, CancellationToken ct = default);

        Task<BaseResponse<JournalEntryDto>> GetJournalEntryByNumberAsync(string entryNumber, CancellationToken ct = default);

        Task<BaseResponse<PagedResult<JournalEntryDto>>> SearchJournalEntriesAsync(
            JournalEntrySearchDto searchDto,
            CancellationToken ct = default);

        Task<BaseResponse<IReadOnlyList<JournalEntryDto>>> GetJournalEntriesByReferenceAsync(
            string referenceType,
            int referenceId,
            CancellationToken ct = default);

        // ============================================
        // AUTOMATED JOURNAL ENTRIES
        // ============================================

        Task<BaseResponse<JournalEntryDto>> CreateJournalEntryFromTransactionAsync(
            int transactionId,
            CancellationToken ct = default);

        Task<BaseResponse<JournalEntryDto>> CreateJournalEntryFromExpenseAsync(
            int expenseId,
            CancellationToken ct = default);

        // ============================================
        // VALIDATION
        // ============================================

        Task<JournalEntryValidationDto> ValidateJournalEntryAsync(
            JournalEntryCreateDto dto,
            CancellationToken ct = default);

        Task<BaseResponse<bool>> CanPostJournalEntryAsync(int id, CancellationToken ct = default);

        Task<BaseResponse<bool>> CanVoidJournalEntryAsync(int id, CancellationToken ct = default);

        // ============================================
        // UTILITIES
        // ============================================

        Task<string> GenerateEntryNumberAsync(CancellationToken ct = default);

        Task<BaseResponse> RecalculateAccountBalancesAsync(CancellationToken ct = default);
    }
}
