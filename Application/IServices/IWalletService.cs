using Application.DTOs;

namespace Application.IServices
{
    public interface IWalletService
    {
        /// <summary>Balance + recent movements. Creates the wallet lazily on first read.</summary>
        Task<WalletSummaryDto> GetSummaryAsync(int userId, int recentCount = 10, CancellationToken ct = default);

        /// <summary>Balances for many users at once (the clients table).</summary>
        Task<Dictionary<int, decimal>> GetBalancesAsync(IReadOnlyList<int> userIds, CancellationToken ct = default);

        Task<PaginatedResponse<WalletTransactionDto>> GetHistoryAsync(
            int userId, int page = 1, int pageSize = 50, CancellationToken ct = default);

        /// <summary>
        /// Loads money in, applies the best active bonus tier, posts the
        /// journal entry (DR 1000 [+ DR 4905 bonus] / CR 2100).
        /// </summary>
        Task<BaseResponse<WalletTopUpResultDto>> TopUpAsync(
            int userId, WalletTopUpRequestDto req, string actor, CancellationToken ct = default);

        /// <summary>
        /// Admin correction or cash refund. Negative delta with CashOut posts
        /// DR 2100 / CR 1000; corrections use 4905 as the counterpart.
        /// </summary>
        Task<BaseResponse<WalletDto>> AdjustAsync(
            int userId, WalletAdjustRequestDto req, string actor, CancellationToken ct = default);

        /// <summary>
        /// Deducts a spend during checkout. Validates the balance, writes the
        /// ledger row linked to the sale, and returns false with a message if
        /// the wallet can't cover it. The SALE's own journal entry handles the
        /// accounting (DR 2100), so this posts none.
        /// </summary>
        Task<BaseResponse<decimal>> SpendAsync(
            int userId, decimal amount, int transactionRecordId, string actor, CancellationToken ct = default);

        // ── Bonus tiers (admin) ──────────────────────────────────────────
        Task<List<WalletBonusTierDto>> GetTiersAsync(bool includeInactive = false, CancellationToken ct = default);
        Task<WalletBonusTierDto> CreateTierAsync(WalletBonusTierUpsertDto dto, CancellationToken ct = default);
        Task<bool> UpdateTierAsync(int id, WalletBonusTierUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteTierAsync(int id, CancellationToken ct = default);
    }
}
