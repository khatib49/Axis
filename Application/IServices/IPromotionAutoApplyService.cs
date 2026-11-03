using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Evaluates happy hour and combos against the CURRENT lines of an open
    /// invoice and books only the difference with what is already applied, so
    /// it can be called after every order / add-items / remove-item and after
    /// a double click without ever discounting twice.
    /// </summary>
    public interface IPromotionAutoApplyService
    {
        /// <param name="actor">Audit name; null = the current user.</param>
        Task<BaseResponse<AutoApplyResultDto>> ApplyAsync(int transactionId, string? actor = null, CancellationToken ct = default);

        /// <summary>What is currently booked on the invoice (open or closed), without re-evaluating.</summary>
        Task<BaseResponse<AppliedPromotionsDto>> GetAppliedAsync(int transactionId, CancellationToken ct = default);
    }
}
