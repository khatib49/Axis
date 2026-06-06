using Application.DTOs;

namespace Application.IServices
{
    public interface IChannelService
    {
        // Admin listings include inactive (hidden) channels via includeHidden.
        // The cashier/UI default omits them.
        Task<IReadOnlyList<ChannelDto>> ListAsync(bool includeHidden, CancellationToken ct = default);
        Task<ChannelDto?> GetAsync(int id, CancellationToken ct = default);
        Task<ChannelDto> CreateAsync(ChannelCreateDto dto, CancellationToken ct = default);
        Task<ChannelDto> UpdateAsync(int id, ChannelUpdateDto dto, CancellationToken ct = default);
        // Soft-hide. Historical transactions keep their ChannelId so reports
        // remain intact; only the pickers exclude hidden channels.
        Task<bool> DeactivateAsync(int id, CancellationToken ct = default);
    }
}
