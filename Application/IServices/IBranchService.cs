using Application.DTOs;

namespace Application.IServices
{
    public interface IBranchService
    {
        Task<IReadOnlyList<BranchDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
        Task<BranchDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<BranchDto>> CreateAsync(BranchCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<BranchDto>> UpdateAsync(int id, BranchUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Branches for the caller: admin → all active; assigned; none → default branch.</summary>
        Task<MyBranchesDto> GetMineAsync(CancellationToken ct = default);

        Task<IReadOnlyList<UserBranchDto>> ListUsersAsync(int branchId, CancellationToken ct = default);
        Task<BaseResponse<UserBranchDto>> AssignUserAsync(int branchId, AssignUserRequest req, CancellationToken ct = default);
        Task<BaseResponse<bool>> UnassignUserAsync(int branchId, int userId, CancellationToken ct = default);

        Task<IReadOnlyList<BranchSettingDto>> GetSettingsAsync(int branchId, CancellationToken ct = default);
        Task<BaseResponse<IReadOnlyList<BranchSettingDto>>> SetSettingsAsync(int branchId, IEnumerable<BranchSettingUpsertDto> settings, CancellationToken ct = default);

        Task<ConsolidatedDashboardDto> GetConsolidatedAsync(DateTime? from, DateTime? to, CancellationToken ct = default);

        /// <summary>Sets BranchId on every transaction that has none (default: the default branch). Returns the row count.</summary>
        Task<BaseResponse<int>> BackfillTransactionsAsync(int? branchId, CancellationToken ct = default);

        /// <summary>
        /// Phase 3: stamps BranchId on every legacy row (BranchId null) of the phase-1/2
        /// branch-aware tables. tables = "all" (default) or a comma-separated subset of
        /// <c>BranchService.BackfillTables</c>. Returns the count per table.
        /// </summary>
        Task<BaseResponse<BranchBackfillResultDto>> BackfillAsync(int? branchId, string? tables, CancellationToken ct = default);
    }
}
