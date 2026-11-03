using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Customer-facing display per register: the till pushes, the screen polls.</summary>
    public interface IDisplayStateService
    {
        Task<List<DisplayStateDto>> ListAsync(CancellationToken ct = default);
        Task<BaseResponse<DisplayStateDto>> CreateAsync(DisplayCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<DisplayStateDto>> RotateTokenAsync(int id, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        /// <summary>Replaces the cart shown on a register's screen; totals are recomputed in USD and LBP at today's rate. Creates the display row on first push.</summary>
        Task<BaseResponse<DisplayStateDto>> PushAsync(string registerName, DisplayPushDto dto, CancellationToken ct = default);
        /// <summary>Polled by the screen. While Awaiting it re-checks the Whish request and flips to Paid.</summary>
        Task<BaseResponse<DisplayViewDto>> ViewAsync(string token, CancellationToken ct = default);
        /// <summary>Starts a Whish request for the register's current invoice and shows its QR on the screen.</summary>
        Task<BaseResponse<WhishStartResultDto>> RequestWhishAsync(string registerName, DisplayRequestWhishDto dto, string successUrl, string cancelUrl, CancellationToken ct = default);
    }
}
