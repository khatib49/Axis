using Application.DTOs;

namespace Application.IServices
{
    public interface ICouponService
    {
        Task<PaginatedResponse<CouponDto>> ListAsync(CouponFilterDto filter, CancellationToken ct = default);
        Task<CouponDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<CouponDto>> CreateAsync(CouponUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<CouponDto>> UpdateAsync(int id, CouponUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<List<CouponRedemptionDto>> GetRedemptionsAsync(int couponId, CancellationToken ct = default);

        /// <summary>Eligibility + computed discount, nothing written.</summary>
        Task<BaseResponse<CouponValidationResultDto>> ValidateAsync(CouponValidateDto dto, CancellationToken ct = default);
        /// <summary>Recomputes against the open invoice and applies the discount once.</summary>
        Task<BaseResponse<CouponRedeemResultDto>> RedeemAsync(CouponRedeemDto dto, CancellationToken ct = default);
    }
}
