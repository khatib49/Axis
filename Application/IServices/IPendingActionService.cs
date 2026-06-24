using Application.DTOs;

namespace Application.IServices
{
    public interface IPendingActionService
    {
        Task<PaginatedResponse<PendingActionDto>> ListAsync(PendingActionsFilterDto filter, CancellationToken ct = default);
        Task<PendingActionDto?> GetAsync(int id, CancellationToken ct = default);

        // Approve → mark Approved, queue execution (WhatsApp blast). Returns final status.
        Task<ActionDecisionResultDto> ApproveAsync(int id, string actor, CancellationToken ct = default);
        Task<ActionDecisionResultDto> RejectAsync(int id, string actor, string? note, CancellationToken ct = default);
    }
}
