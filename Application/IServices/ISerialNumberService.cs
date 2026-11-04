using Application.DTOs;

namespace Application.IServices
{
    public interface ISerialNumberService
    {
        Task<List<SerialSettingDto>> ListSettingsAsync(CancellationToken ct = default);
        Task<SerialSettingDto?> GetSettingAsync(int itemId, CancellationToken ct = default);
        Task<BaseResponse<SerialSettingDto>> SetSettingAsync(int itemId, SerialSettingUpsertDto dto, CancellationToken ct = default);

        Task<BaseResponse<SerialReceiveResultDto>> ReceiveAsync(SerialReceiveDto dto, CancellationToken ct = default);
        Task<List<SerialNumberDto>> ListAsync(int? itemId, string? status, string? search, int? transactionId, CancellationToken ct = default);
        Task<SerialNumberDto?> GetAsync(int id, CancellationToken ct = default);

        /// <summary>Marks the unit Sold on the invoice. Idempotent for the same invoice; refused for any other status.</summary>
        Task<BaseResponse<SerialNumberDto>> SellAsync(SerialSellDto dto, CancellationToken ct = default);
        Task<BaseResponse<SerialNumberDto>> ReturnAsync(int id, SerialStatusChangeDto? dto, CancellationToken ct = default);
        Task<BaseResponse<SerialNumberDto>> MarkFaultyAsync(int id, SerialStatusChangeDto? dto, CancellationToken ct = default);
        /// <summary>Returned / Faulty unit back into the sellable pool.</summary>
        Task<BaseResponse<SerialNumberDto>> RestockAsync(int id, SerialStatusChangeDto? dto, CancellationToken ct = default);

        Task<List<SerialSummaryDto>> SummaryAsync(int? itemId, CancellationToken ct = default);
    }
}
