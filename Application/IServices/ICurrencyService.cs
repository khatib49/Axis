using Application.DTOs;
using Domain.Entities;

namespace Application.IServices
{
    public interface ICurrencyService
    {
        /// <summary>Creates USD (base) + LBP with a default rate when no currency exists yet.</summary>
        Task EnsureSeededAsync(CancellationToken ct = default);
        Task<List<CurrencyDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
        Task<CurrencyDto?> GetAsync(string code, CancellationToken ct = default);
        Task<BaseResponse<CurrencyDto>> CreateAsync(CurrencyCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<CurrencyDto>> UpdateAsync(string code, CurrencyUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<ExchangeRateDto>> SetRateAsync(SetRateDto dto, CancellationToken ct = default);
        Task<List<ExchangeRateDto>> HistoryAsync(string code, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
        Task<BaseResponse<ConversionResultDto>> ConvertAsync(decimal amount, string from, string to, CancellationToken ct = default);
        /// <summary>Latest effective rate (units per 1 base) for a code; 1 for the base currency; null when unknown.</summary>
        Task<decimal?> GetRateAsync(string code, DateTime? asOf = null, CancellationToken ct = default);
        Task<Currency?> GetBaseAsync(CancellationToken ct = default);
        Task<Currency?> GetEntityAsync(string code, CancellationToken ct = default);
    }
}
