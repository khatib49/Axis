using Application.DTOs;

namespace Application.IServices
{
    public interface IEventService
    {
        // ── Admin CRUD ───────────────────────────────────────────────────
        Task<IReadOnlyList<EventDto>> ListAsync(CancellationToken ct = default);
        Task<EventDto?> GetAsync(int id, CancellationToken ct = default);
        Task<EventDto> CreateAsync(EventUpsertDto dto, string? actor, CancellationToken ct = default);
        Task<EventDto> UpdateAsync(int id, EventUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Attach an uploaded video / hero image to the event.</summary>
        Task<bool> SetMediaAsync(int id, string? videoPath, string? heroImagePath, CancellationToken ct = default);

        // ── Public ───────────────────────────────────────────────────────
        /// <summary>Null when the event doesn't exist or isn't published.</summary>
        Task<EventPublicDto?> GetPublicAsync(string key, CancellationToken ct = default);
    }
}
