using Application.DTOs;
using Microsoft.AspNetCore.Http;

namespace Application.IServices
{
    public interface IItemModel3DService
    {
        Task<List<ItemModel3DDto>> ListAsync(CancellationToken ct = default);
        Task<List<PublicItemModelDto>> ListPublicAsync(CancellationToken ct = default);
        /// <summary>glb required on first upload; usdz / poster optional. Replaces the files that are sent.</summary>
        Task<BaseResponse<ItemModel3DDto>> UploadAsync(int itemId, IFormFile? glb, IFormFile? usdz, IFormFile? poster, CancellationToken ct = default);
        Task<BaseResponse<ItemModel3DDto>> SetActiveAsync(int itemId, bool active, CancellationToken ct = default);
        Task<bool> DeleteAsync(int itemId, CancellationToken ct = default);
    }

    public interface IUpsellService
    {
        Task<List<UpsellSuggestionDto>> SuggestAsync(UpsellSuggestRequest req, bool publicOnly, CancellationToken ct = default);
        Task<BaseResponse<bool>> RecordEventAsync(UpsellEventDto dto, bool publicOnly, CancellationToken ct = default);
        Task<UpsellRebuildResultDto> RebuildAsync(int days = 90, bool writePitches = true, CancellationToken ct = default);
        Task<List<UpsellPairDto>> ListPairsAsync(int? itemId, int take, CancellationToken ct = default);
        Task<List<UpsellRuleDto>> ListRulesAsync(CancellationToken ct = default);
        Task<BaseResponse<UpsellRuleDto>> CreateRuleAsync(UpsellRuleUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<UpsellRuleDto>> UpdateRuleAsync(int id, UpsellRuleUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteRuleAsync(int id, CancellationToken ct = default);
        Task<UpsellStatsDto> StatsAsync(int days, CancellationToken ct = default);
    }

    public interface IDeliveryPlatformService
    {
        Task<List<DeliveryPlatformDto>> ListAsync(CancellationToken ct = default);
        Task<BaseResponse<DeliveryPlatformDto>> CreateAsync(DeliveryPlatformUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<DeliveryPlatformDto>> UpdateAsync(int id, DeliveryPlatformUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<List<DeliveryItemMappingDto>> ListMappingsAsync(int platformId, CancellationToken ct = default);
        Task<BaseResponse<DeliveryItemMappingDto>> UpsertMappingAsync(int platformId, DeliveryItemMappingUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteMappingAsync(int platformId, int mappingId, CancellationToken ct = default);
        /// <summary>Menu feed for the app. Secret = the same Delivery.WebhookSecret.{source} the order webhook uses.</summary>
        Task<BaseResponse<DeliveryMenuFeedDto>> MenuFeedAsync(string source, string? providedSecret, string assetBaseUrl, CancellationToken ct = default);
        Task<BaseResponse<DeliveryMenuFeedDto>> MenuPreviewAsync(int platformId, string assetBaseUrl, CancellationToken ct = default);
        /// <summary>Called after the webhook created an order: auto-accept when the platform asks for it.</summary>
        Task OnOrderIngestedAsync(string source, int deliveryOrderId, CancellationToken ct = default);
        Task<DeliveryPushRunResultDto> RunPushesAsync(CancellationToken ct = default);
        Task<List<DeliveryPlatformPushDto>> ListPushesAsync(int platformId, int take, CancellationToken ct = default);
        Task<bool> RetryPushAsync(int platformId, int pushId, CancellationToken ct = default);
        Task<DeliveryCallbackTestDto> TestCallbackAsync(int platformId, CancellationToken ct = default);
    }

    public interface ICardTerminalService
    {
        Task<List<CardTerminalDto>> ListTerminalsAsync(bool includeInactive, CancellationToken ct = default);
        Task<BaseResponse<CardTerminalDto>> CreateTerminalAsync(CardTerminalUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<CardTerminalDto>> UpdateTerminalAsync(int id, CardTerminalUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteTerminalAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<TerminalPaymentDto>> StartAsync(TerminalStartDto dto, CancellationToken ct = default);
        Task<BaseResponse<TerminalPaymentDto>> CheckAsync(int paymentId, CancellationToken ct = default);
        Task<BaseResponse<TerminalPaymentDto>> ConfirmManualAsync(int paymentId, TerminalManualConfirmDto dto, CancellationToken ct = default);
        Task<BaseResponse<TerminalPaymentDto>> CancelAsync(int paymentId, CancellationToken ct = default);
        Task<BaseResponse<TerminalPaymentDto>> CallbackAsync(string? signature, string rawBody, TerminalCallbackDto dto, CancellationToken ct = default);
        Task<TerminalReportDto> ReportAsync(DateTime? from, DateTime? to, int? terminalId, CancellationToken ct = default);
    }
}
