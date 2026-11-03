using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Month-level period locking. Other modules can call EnsureOpenAsync before
    /// posting anything dated inside a month to respect the lock.
    /// </summary>
    public interface IAccountingPeriodService
    {
        /// <summary>Success when the month containing <paramref name="date"/> is not locked.</summary>
        Task<BaseResponse> EnsureOpenAsync(DateTime date, CancellationToken ct = default);
        Task<List<AccountingPeriodDto>> ListAsync(int year, CancellationToken ct = default);
        Task<BaseResponse<AccountingPeriodDto>> LockAsync(PeriodLockRequestDto dto, CancellationToken ct = default);
        Task<BaseResponse<AccountingPeriodDto>> UnlockAsync(int year, int month, CancellationToken ct = default);
    }
}
