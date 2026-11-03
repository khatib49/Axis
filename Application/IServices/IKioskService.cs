using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Self-service kiosk devices + anonymous ordering with X-Kiosk-Token.</summary>
    public interface IKioskService
    {
        Task<List<KioskDeviceDto>> ListDevicesAsync(CancellationToken ct = default);
        Task<BaseResponse<KioskDeviceDto>> CreateDeviceAsync(KioskDeviceUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<KioskDeviceDto>> UpdateDeviceAsync(int id, KioskDeviceUpsertDto dto, CancellationToken ct = default);
        /// <summary>Issues a fresh token (old one stops working immediately).</summary>
        Task<BaseResponse<KioskDeviceDto>> RotateTokenAsync(int id, CancellationToken ct = default);
        Task<bool> DeleteDeviceAsync(int id, CancellationToken ct = default);

        /// <summary>Menu for the kiosk screen; error "Unauthorized" when the token is unknown / inactive.</summary>
        Task<BaseResponse<SelfOrderMenuDto>> GetMenuAsync(string? token, CancellationToken ct = default);
        /// <summary>Creates a Takeaway DeliveryOrder + open invoice. Error "Unauthorized" when the token is unknown / inactive.</summary>
        Task<BaseResponse<KioskOrderResultDto>> PlaceOrderAsync(string? token, KioskOrderRequest req, CancellationToken ct = default);
    }
}
